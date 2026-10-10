using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Coclico.Models.Network;
using Coclico.Services;
using Coclico.Services.Network;
using CommunityToolkit.Mvvm.Input;

namespace Coclico.ViewModels;

public class NetworkOptimizerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly INetworkOptimizerService _netService;
    private readonly NetworkMonitorService? _monitorService;
    private readonly IDisposable? _monitorSubscription;

    private readonly SemaphoreSlim _loadNicLock = new(1, 1);
    private CancellationTokenSource? _cts;

    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = [];
    public ObservableCollection<DnsBenchmarkResult> DnsResults { get; } = [];
    public ObservableCollection<string> OutputLog { get; } = [];
    public ObservableCollection<CalibrationStepProgress> CalibrationSteps { get; } = [];
    public ObservableCollection<CognitiveLogEntry> CognitiveLogs { get; } = [];
    public ObservableCollection<ExperimentRecord> ExperimentsHistory { get; } = [];
    public ObservableCollection<ExperimentRecord> FilteredExperiments { get; } = [];

    public string ParameterSearchText
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                ApplyParameterFilter();
            }
        }
    } = string.Empty;

    public string SelectedScopeFilter
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                ApplyParameterFilter();
            }
        }
    } = "Tous";

    public EngineExecutionMode SelectedExecutionMode
    {
        get;
        set
        {
            field = value;
            if (field is EngineExecutionMode.Safe or EngineExecutionMode.Auto)
            {
                TestSampleCount = 10;
            }
            else if (field is EngineExecutionMode.Expert)
            {
                TestSampleCount = 40;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(ModeTitle));
            OnPropertyChanged(nameof(ModeDescription));
            OnPropertyChanged(nameof(IsSafeMode));
            OnPropertyChanged(nameof(IsExpertMode));
        }
    } = EngineExecutionMode.Auto;

    public bool IsSafeMode
    {
        get => SelectedExecutionMode != EngineExecutionMode.Expert;
        set
        {
            if (value)
            {
                SelectedExecutionMode = EngineExecutionMode.Safe;
            }
        }
    }

    public bool IsExpertMode
    {
        get => SelectedExecutionMode == EngineExecutionMode.Expert;
        set
        {
            if (value)
            {
                SelectedExecutionMode = EngineExecutionMode.Expert;
            }
        }
    }

    public OptimizationProfile SelectedCognitiveProfile
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProfileTitle));
            OnPropertyChanged(nameof(ProfileDescription));
        }
    } = OptimizationProfile.Gaming;

    public int TestSampleCount
    {
        get;
        set
        {
            int clamped = Math.Clamp(value, 5, 40);
            if (field != clamped)
            {
                field = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LiveSamplesRatio));
            }
        }
    } = 10;

    public string ModeTitle => SelectedExecutionMode switch
    {
        EngineExecutionMode.Expert => "Mode Complet (Expert) — 40 tests & 40 valeurs par paramètre",
        EngineExecutionMode.Simulation => "Mode Avancé (Simulation) — Diagnostic sans modification",
        _ => "Mode Recommandé (Sûr) — 10 tests & plus de 10 valeurs par paramètre"
    };

    public string ModeDescription => SelectedExecutionMode switch
    {
        EngineExecutionMode.Expert => "Balayage empirique complet : 40 tests de haute précision par paramètre et 40 valeurs différentes évaluées sur le matériel NDIS, TCP/IP, QoS, IPv6 et MTU.",
        _ => "Optimisation globale sécurisée sans risque de coupure ni freeze : 10 tests de mesures par paramètre et plus de 10 valeurs évaluées. Inclut benchmark DNS, détection MTU et profils rapides."
    };

    public string ProfileTitle => SelectedCognitiveProfile switch
    {
        OptimizationProfile.Gaming => "Profil Gaming Compétitif",
        OptimizationProfile.LowLatency => "Profil Faible Latence & Jitter",
        OptimizationProfile.MaxThroughput => "Profil Débit Maximal Fibre",
        OptimizationProfile.Balanced => "Profil Équilibré Polyvalent",
        _ => "Profil Personnalisé"
    };

    public string ProfileDescription => SelectedCognitiveProfile switch
    {
        OptimizationProfile.Gaming => "Réactivité maximale en jeu : Nagle désactivé, ACK immédiat et priorité CPU 100%.",
        OptimizationProfile.LowLatency => "Anti-Bufferbloat et gigue minimale pour Discord, jeux et streaming.",
        OptimizationProfile.MaxThroughput => "Bande passante maximale pour téléchargements et connexions Fibre.",
        OptimizationProfile.Balanced => "Compromis idéal entre latence, débit et stabilité au quotidien.",
        _ => "Optimisation adaptative."
    };

    public EngineProgressInfo EngineProgress
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
            NotifyLiveTelemetryProperties();
        }
    } = new();

    public bool IsLaboratoireRunning => IsBusy && EngineProgress.CurrentPhase == EnginePhase.SingleParameterExperiments;
    public string LiveCurrentParameterName => !string.IsNullOrEmpty(EngineProgress.CurrentTestedParameter) ? EngineProgress.CurrentTestedParameter : "Initialisation du banc d'essai...";
    public string LiveCurrentParameterExplanation => !string.IsNullOrEmpty(EngineProgress.CurrentTestedParamExplanation) ? EngineProgress.CurrentTestedParamExplanation : "Paramètre matériel réseau.";
    public string LiveCurrentParameterScope => !string.IsNullOrEmpty(EngineProgress.CurrentTestedParamScope) ? EngineProgress.CurrentTestedParamScope : "Pilote NDIS";
    public string LiveCurrentParameterCategory => !string.IsNullOrEmpty(EngineProgress.CurrentTestedParamCategory) ? EngineProgress.CurrentTestedParamCategory : "Réseau";
    public string LiveCurrentTestName => !string.IsNullOrEmpty(EngineProgress.CurrentTestName) ? EngineProgress.CurrentTestName : "Test de Précision";
    public int LiveTestIndex => EngineProgress.CurrentTestIndex;
    public int LiveSampleIndex => EngineProgress.CurrentSampleIndex;
    public string LiveSamplesRatio => $"Échantillon {EngineProgress.CurrentSampleIndex} / {EngineProgress.TotalSamplesPerTest}";
    public string LiveExperimentRatio => $"Paramètre {EngineProgress.CurrentExperiment} / {Math.Max(1, EngineProgress.TotalExperiments)}";

    public double LivePrecisionLatencyMs => EngineProgress.LivePrecisionLatencyMs;
    public double LivePrecisionJitterMs => EngineProgress.LivePrecisionJitterMs;
    public double LivePrecisionThroughputMbps => EngineProgress.LivePrecisionThroughputMbps;
    public double LivePrecisionBufferbloatMs => EngineProgress.LivePrecisionBufferbloatMs;

    public bool HasCompletedSession => ExperimentsHistory.Any(e => e.Decision is ExperimentDecision.Accepted or ExperimentDecision.RejectedRegression or ExperimentDecision.RejectedInstability or ExperimentDecision.RevertedByWatchdog or ExperimentDecision.KeptBaseline);
    public int ValidatedTweaksCount => ExperimentsHistory.Count(e => e.Decision is ExperimentDecision.Accepted or ExperimentDecision.KeptBaseline);
    public int RevertedTweaksCount => ExperimentsHistory.Count(e => e.Decision is ExperimentDecision.RejectedRegression or ExperimentDecision.RejectedInstability or ExperimentDecision.RevertedByWatchdog);
    public int TotalTestedParametersCount => ExperimentsHistory.Count;

    public string BaselinePingDisplay => EngineProgress.BaselineScore > 0 ? $"{ExperimentsHistory.FirstOrDefault(e => e.MetricsBefore != null)?.LatencyBeforeMs ?? EngineProgress.CurrentLatencyMs:F1} ms" : "--";
    public string FinalPingDisplay => EngineProgress.CurrentScore > 0 ? $"{EngineProgress.CurrentLatencyMs:F1} ms" : "--";
    public string PingGainDisplay
    {
        get
        {
            double basePing = ExperimentsHistory.FirstOrDefault(e => e.MetricsBefore != null)?.LatencyBeforeMs ?? 0;
            if (basePing > 0 && EngineProgress.CurrentLatencyMs > 0 && basePing > EngineProgress.CurrentLatencyMs)
            {
                double gain = (basePing - EngineProgress.CurrentLatencyMs) / basePing * 100.0;
                return $"-{gain:F1}%";
            }
            return "0%";
        }
    }

    public string BaselineThroughputDisplay => $"{ExperimentsHistory.FirstOrDefault(e => e.MetricsBefore != null)?.ThroughputBeforeMbps ?? 0:F1} Mbps";
    public string FinalThroughputDisplay => EngineProgress.CurrentThroughputMbps > 0 ? $"{EngineProgress.CurrentThroughputMbps:F1} Mbps" : "--";
    public string ThroughputGainDisplay
    {
        get
        {
            double baseThru = ExperimentsHistory.FirstOrDefault(e => e.MetricsBefore != null)?.ThroughputBeforeMbps ?? 0;
            if (baseThru > 0 && EngineProgress.CurrentThroughputMbps > baseThru)
            {
                double gain = (EngineProgress.CurrentThroughputMbps - baseThru) / baseThru * 100.0;
                return $"+{gain:F1}%";
            }
            return "0%";
        }
    }

    public string BaselineJitterDisplay => $"{ExperimentsHistory.FirstOrDefault(e => e.MetricsBefore != null)?.JitterBeforeMs ?? 0:F1} ms";
    public string FinalJitterDisplay => EngineProgress.CurrentJitterMs > 0 ? $"{EngineProgress.CurrentJitterMs:F1} ms" : "--";

    public string BaselineBufferbloatDisplay => ExperimentsHistory.FirstOrDefault(e => e.MetricsBefore != null)?.BufferbloatBefore ?? "A";
    public string FinalBufferbloatDisplay => EngineProgress.CurrentBufferbloatGrade;

    public string FinalScoreDisplay => EngineProgress.CurrentScore > 0 ? $"{EngineProgress.CurrentScore:F1} / 100" : "--";
    public string BaselineScoreDisplay => EngineProgress.BaselineScore > 0 ? $"{EngineProgress.BaselineScore:F1} / 100" : "--";
    public string ScoreGainDisplay
    {
        get
        {
            if (EngineProgress.CurrentScore > 0 && EngineProgress.BaselineScore > 0)
            {
                double delta = EngineProgress.CurrentScore - EngineProgress.BaselineScore;
                return (delta >= 0 ? "+" : "") + $"{delta:F1} pts";
            }
            return "0 pts";
        }
    }

    public string LiveEstimatedTimeRemaining
    {
        get
        {
            if (!IsBusy)
            {
                return string.Empty;
            }

            if (EngineProgress.CurrentPhase == EnginePhase.Completed)
            {
                return "Terminé";
            }

            if (EngineProgress.CurrentPhase == EnginePhase.BaselineBenchmark)
            {
                return "~15s (Mesure initiale)";
            }

            if (EngineProgress.CurrentPhase == EnginePhase.SingleParameterExperiments)
            {
                int remainingExp = Math.Max(0, EngineProgress.TotalExperiments - EngineProgress.CurrentExperiment);
                int sec = remainingExp * (IsExpertMode ? 4 : 2);
                return sec > 0 ? $"~{sec}s restantes" : "Finalisation...";
            }
            return EngineProgress.CurrentPhase == EnginePhase.ValidationVerification ? "~5s (Validation finale)" : "~30s restantes";
        }
    }

    public string LiveLatencyDeltaText
    {
        get
        {
            if (LivePrecisionLatencyMs > 0 && EngineProgress.BaselineLatencyMs > 0)
            {
                double diff = LivePrecisionLatencyMs - EngineProgress.BaselineLatencyMs;
                return diff <= -0.1
                    ? $"▲ -{Math.Abs(diff):F1} ms (-{Math.Abs(diff) / EngineProgress.BaselineLatencyMs * 100:F0}%)"
                    : diff >= 0.1 ? $"▼ +{diff:F1} ms (+{diff / EngineProgress.BaselineLatencyMs * 100:F0}%)" : "≈ Référence (0.0 ms)";
            }
            return "En attente...";
        }
    }

    public string LiveLatencyDeltaColorHex
    {
        get
        {
            if (LivePrecisionLatencyMs > 0 && EngineProgress.BaselineLatencyMs > 0)
            {
                double diff = LivePrecisionLatencyMs - EngineProgress.BaselineLatencyMs;
                return diff <= -0.1 ? "#10B981" : diff >= 0.1 ? "#EF4444" : "#94A3B8";
            }
            return "#94A3B8";
        }
    }

    public string LiveLatencyDeltaBadge
    {
        get
        {
            if (LivePrecisionLatencyMs > 0 && EngineProgress.BaselineLatencyMs > 0)
            {
                double diff = LivePrecisionLatencyMs - EngineProgress.BaselineLatencyMs;
                return diff <= -0.1 ? "GAIN DÉTECTÉ" : diff >= 0.1 ? "RÉGRESSION ➔ REJET" : "RÉFÉRENCE";
            }
            return "--";
        }
    }

    public string LiveJitterDeltaText
    {
        get
        {
            if (LivePrecisionJitterMs > 0 && EngineProgress.BaselineJitterMs > 0)
            {
                double diff = LivePrecisionJitterMs - EngineProgress.BaselineJitterMs;
                return diff <= -0.05 ? $"▲ -{Math.Abs(diff):F1} ms" : diff >= 0.05 ? $"▼ +{diff:F1} ms" : "≈ Stable";
            }
            return "En attente...";
        }
    }

    public string LiveJitterDeltaColorHex
    {
        get
        {
            if (LivePrecisionJitterMs > 0 && EngineProgress.BaselineJitterMs > 0)
            {
                double diff = LivePrecisionJitterMs - EngineProgress.BaselineJitterMs;
                return diff <= -0.05 ? "#10B981" : diff >= 0.05 ? "#EF4444" : "#94A3B8";
            }
            return "#94A3B8";
        }
    }

    public string LiveThroughputDeltaText
    {
        get
        {
            if (LivePrecisionThroughputMbps > 0 && EngineProgress.BaselineThroughputMbps > 0)
            {
                double diff = LivePrecisionThroughputMbps - EngineProgress.BaselineThroughputMbps;
                return diff >= 1.0
                    ? $"▲ +{diff:F1} Mbps (+{diff / EngineProgress.BaselineThroughputMbps * 100:F0}%)"
                    : diff <= -1.0 ? $"▼ -{Math.Abs(diff):F1} Mbps" : "≈ Débit nominal";
            }
            return "En attente...";
        }
    }

    public string LiveThroughputDeltaColorHex
    {
        get
        {
            if (LivePrecisionThroughputMbps > 0 && EngineProgress.BaselineThroughputMbps > 0)
            {
                double diff = LivePrecisionThroughputMbps - EngineProgress.BaselineThroughputMbps;
                return diff >= 1.0 ? "#10B981" : diff <= -1.0 ? "#EF4444" : "#94A3B8";
            }
            return "#94A3B8";
        }
    }

    public string LiveBufferbloatDeltaText => LivePrecisionBufferbloatMs >= 0
                ? LivePrecisionBufferbloatMs <= 5.0
                    ? "Grade A+ (Zéro lag sous charge)"
                    : LivePrecisionBufferbloatMs <= 15.0
                    ? $"Grade A (+{LivePrecisionBufferbloatMs:F0} ms sous charge)"
                    : $"Grade B (+{LivePrecisionBufferbloatMs:F0} ms sous charge)"
                : "Mesure sous charge...";

    public string LiveBufferbloatDeltaColorHex => LivePrecisionBufferbloatMs >= 0
                ? LivePrecisionBufferbloatMs <= 5.0 ? "#10B981" : LivePrecisionBufferbloatMs <= 15.0 ? "#34D399" : "#EF4444"
                : "#A5B4FC";

    public string LiveDecisionTrendText
    {
        get
        {
            if (LivePrecisionLatencyMs > 0 && EngineProgress.BaselineLatencyMs > 0)
            {
                double diff = LivePrecisionLatencyMs - EngineProgress.BaselineLatencyMs;
                return diff <= -0.1
                    ? "⚡ Tendance très favorable : gain de latence net confirmé"
                    : diff >= 0.2
                    ? "⚠️ Régression détectée : ce réglage sera rejeté par le moteur"
                    : "🔬 Analyse statistique multi-dimensionnelle en cours...";
            }
            return "🔬 Analyse statistique multi-dimensionnelle en cours...";
        }
    }

    public string LiveDecisionTrendColorHex
    {
        get
        {
            if (LivePrecisionLatencyMs > 0 && EngineProgress.BaselineLatencyMs > 0)
            {
                double diff = LivePrecisionLatencyMs - EngineProgress.BaselineLatencyMs;
                return diff <= -0.1 ? "#10B981" : diff >= 0.2 ? "#EF4444" : "#6366F1";
            }
            return "#6366F1";
        }
    }

    public IEnumerable<ExperimentRecord> LiveRecentDecisions =>
        ExperimentsHistory.Where(e => e.Decision != ExperimentDecision.SimulationOnly)
                          .TakeLast(3)
                          .Reverse();

    public IEnumerable<ExperimentRecord> TopImprovements =>
        ExperimentsHistory.Where(e => e.Decision == ExperimentDecision.Accepted || (e.MetricsBefore != null && e.MetricsAfter != null && e.LatencyAfterMs < e.LatencyBeforeMs))
                          .OrderByDescending(e => ((e.LatencyBeforeMs - e.LatencyAfterMs) * 2.0) + e.ScoreDelta)
                          .Take(3);

    public double BaselinePingProgress
    {
        get => Math.Clamp((EngineProgress.BaselineLatencyMs > 0 ? EngineProgress.BaselineLatencyMs : (ExperimentsHistory.FirstOrDefault(e => e.MetricsBefore != null)?.LatencyBeforeMs ?? 20.0)) / 50.0 * 100.0, 10, 100);
        set { }
    }

    public double FinalPingProgress
    {
        get => Math.Clamp((EngineProgress.CurrentLatencyMs > 0 ? EngineProgress.CurrentLatencyMs : 15.0) / 50.0 * 100.0, 10, 100);
        set { }
    }

    public double FinalJitterProgress
    {
        get => Math.Clamp((EngineProgress.CurrentJitterMs > 0 ? EngineProgress.CurrentJitterMs : 0.8) / 5.0 * 100.0, 10, 100);
        set { }
    }

    public double BaselineThroughputProgress
    {
        get => Math.Clamp((EngineProgress.BaselineThroughputMbps > 0 ? EngineProgress.BaselineThroughputMbps : (ExperimentsHistory.FirstOrDefault(e => e.MetricsBefore != null)?.ThroughputBeforeMbps ?? 500.0)) / 1000.0 * 100.0, 10, 100);
        set { }
    }

    public double FinalThroughputProgress
    {
        get => Math.Clamp((EngineProgress.CurrentThroughputMbps > 0 ? EngineProgress.CurrentThroughputMbps : 800.0) / 1000.0 * 100.0, 10, 100);
        set { }
    }

    public double BaselineScoreProgress
    {
        get => Math.Clamp(EngineProgress.BaselineScore > 0 ? EngineProgress.BaselineScore : 60.0, 10, 100);
        set { }
    }

    public double FinalScoreProgress
    {
        get => Math.Clamp(EngineProgress.CurrentScore > 0 ? EngineProgress.CurrentScore : 88.0, 10, 100);
        set { }
    }

    public void NotifyLiveTelemetryProperties()
    {
        OnPropertyChanged(nameof(IsLaboratoireRunning));
        OnPropertyChanged(nameof(LiveCurrentParameterName));
        OnPropertyChanged(nameof(LiveCurrentParameterExplanation));
        OnPropertyChanged(nameof(LiveCurrentParameterScope));
        OnPropertyChanged(nameof(LiveCurrentParameterCategory));
        OnPropertyChanged(nameof(LiveCurrentTestName));
        OnPropertyChanged(nameof(LiveTestIndex));
        OnPropertyChanged(nameof(LiveSampleIndex));
        OnPropertyChanged(nameof(LiveSamplesRatio));
        OnPropertyChanged(nameof(LiveExperimentRatio));
        OnPropertyChanged(nameof(LivePrecisionLatencyMs));
        OnPropertyChanged(nameof(LivePrecisionJitterMs));
        OnPropertyChanged(nameof(LivePrecisionThroughputMbps));
        OnPropertyChanged(nameof(LivePrecisionBufferbloatMs));
        OnPropertyChanged(nameof(LiveEstimatedTimeRemaining));
        OnPropertyChanged(nameof(LiveLatencyDeltaText));
        OnPropertyChanged(nameof(LiveLatencyDeltaColorHex));
        OnPropertyChanged(nameof(LiveLatencyDeltaBadge));
        OnPropertyChanged(nameof(LiveJitterDeltaText));
        OnPropertyChanged(nameof(LiveJitterDeltaColorHex));
        OnPropertyChanged(nameof(LiveThroughputDeltaText));
        OnPropertyChanged(nameof(LiveThroughputDeltaColorHex));
        OnPropertyChanged(nameof(LiveBufferbloatDeltaText));
        OnPropertyChanged(nameof(LiveBufferbloatDeltaColorHex));
        OnPropertyChanged(nameof(LiveDecisionTrendText));
        OnPropertyChanged(nameof(LiveDecisionTrendColorHex));
        OnPropertyChanged(nameof(LiveRecentDecisions));
        OnPropertyChanged(nameof(TopImprovements));
        OnPropertyChanged(nameof(BaselinePingProgress));
        OnPropertyChanged(nameof(FinalPingProgress));
        OnPropertyChanged(nameof(BaselineThroughputProgress));
        OnPropertyChanged(nameof(FinalThroughputProgress));
        OnPropertyChanged(nameof(BaselineScoreProgress));
        OnPropertyChanged(nameof(FinalScoreProgress));
        OnPropertyChanged(nameof(HasCompletedSession));
        OnPropertyChanged(nameof(ValidatedTweaksCount));
        OnPropertyChanged(nameof(RevertedTweaksCount));
        OnPropertyChanged(nameof(TotalTestedParametersCount));
        OnPropertyChanged(nameof(BaselinePingDisplay));
        OnPropertyChanged(nameof(FinalPingDisplay));
        OnPropertyChanged(nameof(PingGainDisplay));
        OnPropertyChanged(nameof(BaselineThroughputDisplay));
        OnPropertyChanged(nameof(FinalThroughputDisplay));
        OnPropertyChanged(nameof(ThroughputGainDisplay));
        OnPropertyChanged(nameof(BaselineJitterDisplay));
        OnPropertyChanged(nameof(FinalJitterDisplay));
        OnPropertyChanged(nameof(BaselineBufferbloatDisplay));
        OnPropertyChanged(nameof(FinalBufferbloatDisplay));
        OnPropertyChanged(nameof(FinalScoreDisplay));
        OnPropertyChanged(nameof(BaselineScoreDisplay));
        OnPropertyChanged(nameof(ScoreGainDisplay));
        OnPropertyChanged(nameof(LiveThroughputMbps));
        OnPropertyChanged(nameof(LiveThroughputDisplay));
    }

    public void ApplyParameterFilter()
    {
        void FilterAction()
        {
            FilteredExperiments.Clear();
            IEnumerable<ExperimentRecord> query = ExperimentsHistory.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(ParameterSearchText))
            {
                query = query.Where(e =>
                    e.ParameterDisplayName.Contains(ParameterSearchText, StringComparison.OrdinalIgnoreCase) ||
                    e.ParameterKeyword.Contains(ParameterSearchText, StringComparison.OrdinalIgnoreCase) ||
                    e.ParameterExplanation.Contains(ParameterSearchText, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(SelectedScopeFilter) && SelectedScopeFilter != "Tous")
            {
                query = query.Where(e => e.ParameterScope.Contains(SelectedScopeFilter, StringComparison.OrdinalIgnoreCase));
            }

            foreach (ExperimentRecord item in query)
            {
                FilteredExperiments.Add(item);
            }
        }

        SafeInvokeOnDispatcher(FilterAction);
    }

    private static void SafeInvokeOnDispatcher(Action action)
    {
        if (Application.Current?.Dispatcher != null &&
            !Application.Current.Dispatcher.CheckAccess() &&
            !Application.Current.Dispatcher.HasShutdownStarted &&
            !Application.Current.Dispatcher.HasShutdownFinished)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(action);
                return;
            }
            catch (TaskCanceledException) { }
        }
        action();
    }

    public string StorytellingReport
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStorytellingReport));
        }
    } = string.Empty;

    public bool HasStorytellingReport => !string.IsNullOrWhiteSpace(StorytellingReport);

    public int SelectedTabIndex
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsLaboratoireTab));
                OnPropertyChanged(nameof(IsAutoOptTab));
                OnPropertyChanged(nameof(IsMatrixTab));
                OnPropertyChanged(nameof(IsAdvancedTab));
                OnPropertyChanged(nameof(IsResultsTab));
                OnPropertyChanged(nameof(IsStatsTab));
                OnPropertyChanged(nameof(IsLogsTab));
                OnPropertyChanged(nameof(IsToolsTab));
                OnPropertyChanged(nameof(IsOverviewTab));
                OnPropertyChanged(nameof(IsCalibrationTab));
                OnPropertyChanged(nameof(IsTcpTab));
                OnPropertyChanged(nameof(IsNicTab));
                OnPropertyChanged(nameof(IsDnsTab));
                OnPropertyChanged(nameof(IsCognitiveTab));
            }
        }
    } = 0;

    public bool IsLaboratoireTab
    {
        get => SelectedTabIndex == 0;
        set
        {
            if (value)
            {
                SelectedTabIndex = 0;
            }
        }
    }

    public bool IsAutoOptTab => IsLaboratoireTab;

    public bool IsMatrixTab
    {
        get => SelectedTabIndex == 1;
        set
        {
            if (value)
            {
                SelectedTabIndex = 1;
            }
        }
    }

    public bool IsAdvancedTab => IsMatrixTab;

    public bool IsResultsTab
    {
        get => SelectedTabIndex == 2;
        set
        {
            if (value)
            {
                SelectedTabIndex = 2;
            }
        }
    }

    public bool IsStatsTab => IsResultsTab;

    public bool IsLogsTab
    {
        get => SelectedTabIndex == 3;
        set
        {
            if (value)
            {
                SelectedTabIndex = 3;
            }
        }
    }

    public bool IsToolsTab
    {
        get => false;
        set
        {
            if (value)
            {
                SelectedTabIndex = 0;
            }
        }
    }

    public bool IsOverviewTab => SelectedTabIndex == 0;
    public bool IsCalibrationTab => SelectedTabIndex == 1;
    public bool IsTcpTab => SelectedTabIndex == 2;
    public bool IsNicTab => SelectedTabIndex == 3;
    public bool IsDnsTab => SelectedTabIndex == 4;
    public bool IsCognitiveTab => SelectedTabIndex == 0;

    public bool IsDeepCalibration
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = true;

    public TcpGlobalSettings TcpSettings
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = new();

    public NicHardwareSettings NicSettings
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = new();

    public bool IsBusy
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotBusy));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public string StatusMessage
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = "Prêt à optimiser la connexion et les cartes réseau.";

    public double ProgressPercent
    {
        get;
        set { field = value; OnPropertyChanged(); }
    }

    public NetworkAdapterInfo? SelectedAdapter
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedAdapter));
                OnPropertyChanged(nameof(LiveThroughputMbps));
                OnPropertyChanged(nameof(LiveThroughputDisplay));
                _ = LoadNicSettingsForSelectedAdapterAsync();
            }
        }
    }

    public bool HasSelectedAdapter => SelectedAdapter != null;

    public NetworkOptimizationPreset SelectedPreset
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = NetworkOptimizationPreset.GamingUltra;

    public NetworkBenchmarkReport? LastReport
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLastReport));
        }
    }

    public bool HasLastReport => LastReport != null;

    public MtuDetectionResult? MtuResult
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMtuResult));
        }
    }

    public bool HasMtuResult => MtuResult != null;

    public double LivePing
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = 0;

    public double LiveJitter
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = 0;

    public double LiveDownloadKbps
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LiveThroughputMbps));
            OnPropertyChanged(nameof(LiveThroughputDisplay));
        }
    } = 0;

    public double LiveUploadKbps
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = 0;

    public double LiveThroughputMbps => EngineProgress.CurrentThroughputMbps > 0
        ? EngineProgress.CurrentThroughputMbps
        : EngineProgress.LivePrecisionThroughputMbps > 0
        ? EngineProgress.LivePrecisionThroughputMbps
        : LiveDownloadKbps > 0
        ? Math.Round(LiveDownloadKbps / 1000.0, 1)
        : SelectedAdapter != null && SelectedAdapter.SpeedBitsPerSecond > 0
        ? Math.Round(SelectedAdapter.SpeedBitsPerSecond / 1_000_000.0, 1)
        : 0;

    public string LiveThroughputDisplay
    {
        get
        {
            if (EngineProgress.CurrentThroughputMbps > 0)
            {
                return $"{EngineProgress.CurrentThroughputMbps:F1} Mbps";
            }

            return EngineProgress.LivePrecisionThroughputMbps > 0
                ? $"{EngineProgress.LivePrecisionThroughputMbps:F1} Mbps"
                : SelectedAdapter != null && !string.IsNullOrEmpty(SelectedAdapter.SpeedDisplay) && SelectedAdapter.SpeedDisplay != "Non connecté"
                ? SelectedAdapter.SpeedDisplay
                : LiveDownloadKbps >= 1000.0
                ? $"{LiveDownloadKbps / 1000.0:F1} Mbps"
                : LiveDownloadKbps > 0 ? $"{LiveDownloadKbps:F0} Kbps" : "--";
        }
    }

    public IRelayCommand<int> SelectTabCommand { get; }
    public IAsyncRelayCommand RefreshAdaptersCommand { get; }
    public IAsyncRelayCommand ApplyPresetCommand { get; }
    public IAsyncRelayCommand RunAutoTuningCommand { get; }
    public IAsyncRelayCommand RunComprehensiveCalibrationCommand { get; }
    public IAsyncRelayCommand SaveTcpSettingsCommand { get; }
    public IAsyncRelayCommand ReloadTcpSettingsCommand { get; }
    public IAsyncRelayCommand SaveNicSettingsCommand { get; }
    public IAsyncRelayCommand ReloadNicSettingsCommand { get; }
    public IAsyncRelayCommand BenchmarkDnsCommand { get; }
    public IAsyncRelayCommand RunCognitiveOptimizationCommand { get; }
    public IAsyncRelayCommand RollbackEmergencyCommand { get; }
    public IAsyncRelayCommand ApplyFastestDnsCommand { get; }
    public IAsyncRelayCommand DetectMtuCommand { get; }
    public IAsyncRelayCommand ApplyOptimalMtuCommand { get; }
    public IAsyncRelayCommand RestoreDefaultsCommand { get; }
    public IRelayCommand CopyResultsSummaryCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public NetworkOptimizerViewModel() : this(
        ServiceContainer.GetOptional<INetworkOptimizerService>() ?? new NetworkOptimizerService(),
        ServiceContainer.GetOptional<NetworkMonitorService>())
    {
    }

    public NetworkOptimizerViewModel(INetworkOptimizerService netService, NetworkMonitorService? monitorService = null)
    {
        _netService = netService;
        _monitorService = monitorService;

        SelectTabCommand = new RelayCommand<int>(idx => SelectedTabIndex = idx);
        RefreshAdaptersCommand = new AsyncRelayCommand(RefreshAdaptersAsync);
        RunCognitiveOptimizationCommand = new AsyncRelayCommand(RunCognitiveOptimizationAsync, () => IsNotBusy);
        RollbackEmergencyCommand = new AsyncRelayCommand(RollbackEmergencyAsync, () => IsNotBusy);
        ApplyPresetCommand = new AsyncRelayCommand(ApplyPresetAsync, () => IsNotBusy);
        RunAutoTuningCommand = new AsyncRelayCommand(RunAutoTuningAsync, () => IsNotBusy);
        RunComprehensiveCalibrationCommand = new AsyncRelayCommand(RunComprehensiveCalibrationAsync, () => IsNotBusy);
        SaveTcpSettingsCommand = new AsyncRelayCommand(SaveTcpSettingsAsync, () => IsNotBusy);
        ReloadTcpSettingsCommand = new AsyncRelayCommand(ReloadTcpSettingsAsync, () => IsNotBusy);
        SaveNicSettingsCommand = new AsyncRelayCommand(SaveNicSettingsAsync, () => IsNotBusy && HasSelectedAdapter);
        ReloadNicSettingsCommand = new AsyncRelayCommand(ReloadNicSettingsAsync, () => IsNotBusy && HasSelectedAdapter);
        BenchmarkDnsCommand = new AsyncRelayCommand(BenchmarkDnsAsync, () => IsNotBusy);
        ApplyFastestDnsCommand = new AsyncRelayCommand(ApplyFastestDnsAsync, () => IsNotBusy && DnsResults.Any(d => d.IsFastest));
        DetectMtuCommand = new AsyncRelayCommand(DetectMtuAsync, () => IsNotBusy);
        ApplyOptimalMtuCommand = new AsyncRelayCommand(ApplyOptimalMtuAsync, () => IsNotBusy && HasMtuResult && HasSelectedAdapter);
        RestoreDefaultsCommand = new AsyncRelayCommand(RestoreDefaultsAsync, () => IsNotBusy);
        CopyResultsSummaryCommand = new RelayCommand(CopyResultsSummary);
        CancelCommand = new RelayCommand(CancelExecution, () => IsBusy);

        if (_monitorService != null)
        {
            _monitorSubscription = _monitorService.StatsStream.Subscribe(stats =>
            {
                _ = (Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    if (stats.PingMs >= 0)
                    {
                        LivePing = stats.PingMs;
                    }

                    LiveDownloadKbps = stats.DownloadKbps;
                    LiveUploadKbps = stats.UploadKbps;
                }));
            });
        }

        _ = RefreshAdaptersAsync();
        _ = ReloadTcpSettingsAsync();
    }

    public async Task RefreshAdaptersAsync()
    {
        try
        {
            IReadOnlyList<NetworkAdapterInfo> adapters = await _netService.GetNetworkAdaptersAsync();

            void UpdateAdapters()
            {
                Adapters.Clear();
                foreach (NetworkAdapterInfo a in adapters)
                {
                    Adapters.Add(a);
                }

                NetworkAdapterInfo? defaultAdapter = Adapters.FirstOrDefault(a => a.IsActive) ?? Adapters.FirstOrDefault();
                if (SelectedAdapter != defaultAdapter)
                {
                    SelectedAdapter = defaultAdapter;
                }
            }

            SafeInvokeOnDispatcher(UpdateAdapters);

            await LoadNicSettingsForSelectedAdapterAsync();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.RefreshAdaptersAsync");
        }
    }

    public async Task ReloadTcpSettingsAsync()
    {
        try
        {
            TcpGlobalSettings tcp = await _netService.GetTcpSettingsAsync();
            TcpSettings = tcp;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.ReloadTcpSettingsAsync");
        }
    }

    public async Task LoadNicSettingsForSelectedAdapterAsync()
    {
        if (SelectedAdapter == null)
        {
            return;
        }

        await _loadNicLock.WaitAsync();
        try
        {
            NicHardwareSettings nic = await _netService.GetNicHardwareSettingsAsync(SelectedAdapter.Id);
            nic.AdapterName = SelectedAdapter.Name;
            NicSettings = nic;

            if (ExperimentsHistory.Count == 0)
            {
                INetworkSafetyService safety = ServiceContainer.GetOptional<INetworkSafetyService>() ?? new NetworkSafetyService();
                IReadOnlyList<DynamicNetworkParameter> discovered = await _netService.DiscoveryService.DiscoverAdapterParametersAsync(SelectedAdapter.Id, SelectedAdapter.Name);
                int idx = 1;
                var list = new List<ExperimentRecord>();
                foreach (DynamicNetworkParameter p in discovered)
                {
                    safety.ClassifyParameter(p);
                    var rec = new ExperimentRecord
                    {
                        ExperimentIndex = idx++,
                        ParameterKeyword = p.RegistryKeyword,
                        ParameterDisplayName = p.DisplayName,
                        PreviousValue = p.CurrentValue,
                        TestedValue = "--",
                        SafetyLevel = p.SafetyLevel,
                        ParameterScope = p.Scope,
                        ParameterCategoryName = p.Category.ToString(),
                        ParameterExplanation = safety.GetExplanationForParameter(p),
                        Decision = ExperimentDecision.SimulationOnly,
                        DecisionReason = "Prêt pour le banc d'essai",
                        SamplesTestedCount = TestSampleCount
                    };
                    list.Add(rec);
                }

                void UpdateUI()
                {
                    ExperimentsHistory.Clear();
                    foreach (ExperimentRecord item in list)
                    {
                        ExperimentsHistory.Add(item);
                    }
                    ApplyParameterFilter();
                    NotifyLiveTelemetryProperties();
                }

                SafeInvokeOnDispatcher(UpdateUI);
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.LoadNicSettingsForSelectedAdapterAsync");
        }
        finally
        {
            _ = _loadNicLock.Release();
        }
    }

    private async Task ReloadNicSettingsAsync()
    {
        await LoadNicSettingsForSelectedAdapterAsync();
        ToastService.Show("Propriétés matérielles de la carte rechargées.");
    }

    private async Task SaveTcpSettingsAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            AppendLog("⚙️ Application des paramètres TCP/IP personnalisés...");
            bool ok = await _netService.ApplyCustomTcpSettingsAsync(TcpSettings, SelectedAdapter?.Id, _cts.Token);
            if (ok)
            {
                ToastService.Show("Paramètres TCP/IP personnalisés appliqués avec succès !");
                AppendLog("✅ Paramètres TCP/IP enregistrés avec succès.");
                await ReloadTcpSettingsAsync();
            }
            else
            {
                ToastService.Show("Erreur lors de l'application des paramètres TCP/IP.");
                AppendLog("❌ Échec d'application TCP/IP.");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.SaveTcpSettingsAsync");
            StatusMessage = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveNicSettingsAsync()
    {
        if (SelectedAdapter == null)
        {
            return;
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            AppendLog($"🖧 Application des propriétés matérielles sur {SelectedAdapter.Name} ({NicSettings.AdapterName})...");
            bool ok = await _netService.ApplyCustomNicSettingsAsync(SelectedAdapter.Id, NicSettings, _cts.Token);
            if (ok)
            {
                ToastService.Show($"Propriétés matérielles appliquées sur {SelectedAdapter.Name} !");
                AppendLog($"✅ Carte {SelectedAdapter.Name} reconfigurée (Interrupt Moderation, Offloads, Buffers).");
                await LoadNicSettingsForSelectedAdapterAsync();
            }
            else
            {
                ToastService.Show("Erreur lors de la configuration de la carte.");
                AppendLog("❌ Échec d'application sur la carte réseau.");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.SaveNicSettingsAsync");
            StatusMessage = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunComprehensiveCalibrationAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        OutputLog.Clear();
        CalibrationSteps.Clear();
        ProgressPercent = 0;

        try
        {
            AppendLog($"🧪 Démarrage de la Calibration Approfondie (Mode {(IsDeepCalibration ? "Complet 8 étapes (~45s)" : "Rapide (~15s)")})...");
            NetworkBenchmarkReport report = await _netService.RunComprehensiveCalibrationAsync(
                SelectedAdapter?.Id,
                IsDeepCalibration,
                step =>
                {
                    _ = (Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        CalibrationStepProgress? existing = CalibrationSteps.FirstOrDefault(s => s.StepIndex == step.StepIndex);
                        if (existing != null)
                        {
                            existing.Status = step.Status;
                            existing.MetricValue = step.MetricValue;
                            existing.Description = step.Description;
                            OnPropertyChanged(nameof(CalibrationSteps));
                        }
                        else
                        {
                            CalibrationSteps.Add(step);
                        }
                    }));
                },
                (msg, pct) =>
                {
                    StatusMessage = msg;
                    ProgressPercent = pct;
                    AppendLog(msg);
                },
                _cts.Token);

            LastReport = report;
            ToastService.Show(report.SummaryText);
            await RefreshAdaptersAsync();
            await ReloadTcpSettingsAsync();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Calibration annulée.";
            AppendLog("⚠️ Calibration interrompue par l'utilisateur.");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.RunComprehensiveCalibrationAsync");
            StatusMessage = $"Erreur : {ex.Message}";
            AppendLog($"❌ Erreur : {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyPresetAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        OutputLog.Clear();
        ProgressPercent = 0;

        try
        {
            AppendLog($"🚀 Lancement de l'optimisation en mode {SelectedPreset}...");
            NetworkBenchmarkReport report = await _netService.ApplyPresetAsync(
                SelectedPreset,
                SelectedAdapter?.Id,
                (msg, pct) =>
                {
                    StatusMessage = msg;
                    ProgressPercent = pct;
                    AppendLog(msg);
                },
                _cts.Token);

            LastReport = report;
            ToastService.Show(report.SummaryText);
            await RefreshAdaptersAsync();
            await ReloadTcpSettingsAsync();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Opération annulée par l'utilisateur.";
            AppendLog("⚠️ Opération interrompue.");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.ApplyPresetAsync");
            StatusMessage = $"Erreur : {ex.Message}";
            AppendLog($"❌ Erreur : {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunAutoTuningAsync()
    {
        await RunComprehensiveCalibrationAsync();
    }

    private async Task BenchmarkDnsAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        DnsResults.Clear();
        ProgressPercent = 0;

        try
        {
            AppendLog("⚡ Démarrage du Benchmark DNS multi-fournisseurs...");
            IReadOnlyList<DnsBenchmarkResult> results = await _netService.RunDnsBenchmarkAsync(
                (msg, pct) =>
                {
                    StatusMessage = msg;
                    ProgressPercent = pct;
                },
                _cts.Token);

            void AddDns()
            {
                foreach (DnsBenchmarkResult r in results)
                {
                    DnsResults.Add(r);
                }
            }

            SafeInvokeOnDispatcher(AddDns);

            foreach (DnsBenchmarkResult r in results)
            {
                AppendLog($"DNS {r.ProviderName} : {r.DisplayLatency}");
            }

            DnsBenchmarkResult? fastest = results.FirstOrDefault(r => r.IsFastest);
            if (fastest != null)
            {
                StatusMessage = $"Le DNS le plus rapide est {fastest.ProviderName} ({fastest.LatencyMs:F1} ms).";
                ToastService.Show(StatusMessage);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Benchmark DNS annulé.";
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.BenchmarkDnsAsync");
            StatusMessage = $"Erreur DNS : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyFastestDnsAsync()
    {
        DnsBenchmarkResult? fastest = DnsResults.FirstOrDefault(d => d.IsFastest);
        if (fastest == null || SelectedAdapter == null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            AppendLog($"🌐 Application du DNS {fastest.ProviderName} sur {SelectedAdapter.Name}...");
            bool ok = await _netService.ApplyDnsAsync(SelectedAdapter.Name, fastest.PrimaryIp, fastest.SecondaryIp);
            if (ok)
            {
                ToastService.Show($"DNS {fastest.ProviderName} appliqué avec succès !");
                AppendLog($"✅ DNS {fastest.PrimaryIp} configuré avec succès.");
                await RefreshAdaptersAsync();
            }
            else
            {
                ToastService.Show("Impossible d'appliquer le DNS.");
                AppendLog("❌ Échec de l'application du DNS.");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.ApplyFastestDnsAsync");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DetectMtuAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        ProgressPercent = 0;

        try
        {
            AppendLog("📐 Balayage de la taille de paquet maximale sans fragmentation...");
            MtuDetectionResult res = await _netService.DetectOptimalMtuAsync(
                progress: (msg, pct) =>
                {
                    StatusMessage = msg;
                    ProgressPercent = pct;
                },
                ct: _cts.Token);

            MtuResult = res;
            AppendLog($"✅ {res.Details}");
            ToastService.Show($"MTU optimal détecté : {res.OptimalMtu} octets");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Détection MTU annulée.";
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.DetectMtuAsync");
            StatusMessage = $"Erreur MTU : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyOptimalMtuAsync()
    {
        if (MtuResult == null || SelectedAdapter == null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            AppendLog($"⚙️ Application du MTU {MtuResult.OptimalMtu} sur {SelectedAdapter.Name}...");
            bool ok = await _netService.ApplyMtuAsync(SelectedAdapter.Name, MtuResult.OptimalMtu);
            if (ok)
            {
                ToastService.Show($"MTU {MtuResult.OptimalMtu} appliqué avec succès !");
                AppendLog($"✅ MTU {MtuResult.OptimalMtu} enregistré de manière persistante.");
                await RefreshAdaptersAsync();
            }
            else
            {
                ToastService.Show("Impossible d'appliquer le MTU.");
                AppendLog("❌ Échec de configuration du MTU.");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.ApplyOptimalMtuAsync");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunCognitiveOptimizationAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        CognitiveLogs.Clear();
        ExperimentsHistory.Clear();
        ProgressPercent = 0;
        StorytellingReport = string.Empty;

        try
        {
            AppendLog($"🧠 Lancement du Moteur Cognitif Réseau (Profil: {SelectedCognitiveProfile}, Mode: {SelectedExecutionMode})...");
            StatusMessage = "Initialisation du moteur cognitif adaptatif...";

            string reportText = await _netService.RunGlobalUnifiedOptimizationAsync(
                SelectedAdapter?.Id,
                SelectedCognitiveProfile,
                SelectedExecutionMode,
                onProgress: progressInfo =>
                {
                    _ = (Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        EngineProgress = progressInfo;
                        StatusMessage = progressInfo.StatusMessage;
                        ProgressPercent = progressInfo.OverallProgress;
                        if (progressInfo.CurrentScore > 0)
                        {
                            LivePing = progressInfo.CurrentLatencyMs;
                            LiveJitter = progressInfo.CurrentJitterMs;
                        }

                        foreach (ExperimentRecord exp in progressInfo.CompletedExperiments)
                        {
                            ExperimentRecord? existing = ExperimentsHistory.FirstOrDefault(e => e.ParameterKeyword.Equals(exp.ParameterKeyword, StringComparison.OrdinalIgnoreCase));
                            if (existing != null)
                            {
                                int idx = ExperimentsHistory.IndexOf(existing);
                                ExperimentsHistory[idx] = exp;
                            }
                            else
                            {
                                ExperimentsHistory.Add(exp);
                            }
                        }
                        ApplyParameterFilter();
                        NotifyLiveTelemetryProperties();
                    }));
                },
                onLog: logEntry =>
                {
                    _ = (Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        CognitiveLogs.Add(logEntry);
                        AppendLog($"[{logEntry.Level}] {logEntry.Message}");
                    }));
                },
                ct: _cts.Token);

            StorytellingReport = reportText;
            ToastService.Show("Optimisation cognitive achevée avec succès !");

            Application.Current?.Dispatcher.Invoke(() =>
            {
                try
                {
                    var win = new Views.NetworkResultsWindow
                    {
                        Owner = Application.Current.MainWindow
                    };
                    win.SetResults(
                        EngineProgress.CurrentLatencyMs > 0 ? EngineProgress.CurrentLatencyMs : LivePing,
                        EngineProgress.CurrentJitterMs > 0 ? EngineProgress.CurrentJitterMs : LiveJitter,
                        EngineProgress.CurrentThroughputMbps,
                        EngineProgress.CurrentBufferbloatGrade,
                        reportText,
                        ExperimentsHistory
                    );
                    _ = win.ShowDialog();
                    SelectedTabIndex = 2;
                }
                catch (Exception ex)
                {
                    LoggingService.LogException(ex, "NetworkResultsWindow.ShowDialog");
                    SelectedTabIndex = 2;
                }
            });

            await RefreshAdaptersAsync();
            await ReloadTcpSettingsAsync();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Session cognitive annulée.";
            AppendLog("⚠️ Session interrompue par l'utilisateur.");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.RunCognitiveOptimizationAsync");
            StatusMessage = $"Erreur cognitive : {ex.Message}";
            AppendLog($"❌ Erreur : {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void CopyResultsSummary()
    {
        try
        {
            var sb = new StringBuilder();
            _ = sb.AppendLine("╔══════════════════════════════════════════════════════════════════╗");
            _ = sb.AppendLine("║             BILAN D'OPTIMISATION RÉSEAU — COCLICO                ║");
            _ = sb.AppendLine("╚══════════════════════════════════════════════════════════════════╝");
            _ = sb.AppendLine();
            _ = sb.AppendLine($"• Adaptateur Réseau : {SelectedAdapter?.Name ?? "Carte active"}");
            _ = sb.AppendLine($"• Profil Appliqué   : {ProfileTitle}");
            _ = sb.AppendLine($"• Score Réseau      : {FinalScoreDisplay} ({ScoreGainDisplay})");
            _ = sb.AppendLine();
            _ = sb.AppendLine("📊 RÉSULTATS COMPARATIFS (AVANT ➔ APRÈS) :");
            _ = sb.AppendLine($"  - Latence Ping    : {BaselinePingDisplay} ➔ {FinalPingDisplay} (Gain : {PingGainDisplay})");
            _ = sb.AppendLine($"  - Gigue (Jitter)  : {BaselineJitterDisplay} ➔ {FinalJitterDisplay}");
            _ = sb.AppendLine($"  - Débit Réel CDN  : {BaselineThroughputDisplay} ➔ {FinalThroughputDisplay} (Gain : {ThroughputGainDisplay})");
            _ = sb.AppendLine($"  - Bufferbloat     : Grade {BaselineBufferbloatDisplay} ➔ Grade {FinalBufferbloatDisplay}");
            _ = sb.AppendLine();
            _ = sb.AppendLine("🛡️ CALIBRATION ET SÉCURITÉ :");
            _ = sb.AppendLine($"  - Réglages validés & appliqués       : {ValidatedTweaksCount}");
            _ = sb.AppendLine($"  - Régressions et instabilités évitées : {RevertedTweaksCount}");
            _ = sb.AppendLine($"  - Total des paramètres évalués       : {TotalTestedParametersCount}");
            _ = sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(StorytellingReport))
            {
                _ = sb.AppendLine("📝 COMPTE-RENDU DÉTAILLÉ :");
                _ = sb.AppendLine(StorytellingReport);
            }

            Clipboard.SetText(sb.ToString());
            ToastService.Show("Bilan d'optimisation copié dans le presse-papier !");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CopyResultsSummary");
            ToastService.Show("Erreur lors de la copie du bilan.");
        }
    }

    private async Task RollbackEmergencyAsync()
    {
        IsBusy = true;
        try
        {
            AppendLog("🛡️ Restauration d'urgence du snapshot initial (Rollback Watchdog)...");
            bool ok = await _netService.SnapshotService.RollbackToBaselineAsync(CancellationToken.None);
            if (ok)
            {
                ToastService.Show("Snapshot initial restauré avec succès !");
                AppendLog("✅ Configuration réseau initiale rétablie.");
                await RefreshAdaptersAsync();
                await ReloadTcpSettingsAsync();
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.RollbackEmergencyAsync");
            StatusMessage = $"Erreur rollback : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreDefaultsAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        OutputLog.Clear();
        ProgressPercent = 0;

        try
        {
            AppendLog("🔄 Restauration de la pile réseau et cartes aux paramètres d'origine Windows...");
            bool ok = await _netService.RestoreWindowsDefaultsAsync(
                SelectedAdapter?.Name,
                (msg, pct) =>
                {
                    StatusMessage = msg;
                    ProgressPercent = pct;
                    AppendLog(msg);
                },
                _cts.Token);

            if (ok)
            {
                ToastService.Show("Paramètres d'origine Windows restaurés avec succès !");
                AppendLog("✅ Rétablissement d'origine terminé.");
                await RefreshAdaptersAsync();
                await ReloadTcpSettingsAsync();
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Restauration annulée.";
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.RestoreDefaultsAsync");
            StatusMessage = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CancelExecution()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.CancelExecution");
        }
    }

    private void AppendLog(string message)
    {
        _ = (Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            OutputLog.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        }));
    }

    public void Dispose()
    {
        try
        {
            _monitorSubscription?.Dispose();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.Dispose:MonitorSubscription");
        }
        try
        {
            _cts?.Dispose();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.Dispose:Cts");
        }
        try
        {
            _loadNicLock.Dispose();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.Dispose:LoadNicLock");
        }
        GC.SuppressFinalize(this);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
