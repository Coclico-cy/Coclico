using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Coclico.Modules.Power.Services;
using Coclico.Services;

namespace Coclico.Modules.Power.Views;

public partial class PowerOptimizerView : UserControl
{
    private readonly CpuPowerService _cpuPower = ServiceContainer.GetRequired<CpuPowerService>();
    private readonly AdaptivePowerService _adaptive = ServiceContainer.GetRequired<AdaptivePowerService>();
    private readonly DispatcherTimer _gaugeTimer;
    private readonly Action<string> _onAdaptiveStatus;
    private bool _benchmarkRunning;
    private bool _refreshing;

    public PowerOptimizerView()
    {
        InitializeComponent();

        _onAdaptiveStatus = OnAdaptiveStatusChanged;
        _adaptive.StatusChanged += _onAdaptiveStatus;

        _gaugeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _gaugeTimer.Tick += (_, _) => UpdateCpuGauge();
        _gaugeTimer.Start();

        Unloaded += OnUnloaded;
        Loaded += async (_, _) => await LoadStateAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _gaugeTimer.Stop();
        _adaptive.StatusChanged -= _onAdaptiveStatus;
    }

    private async Task LoadStateAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            PowerStateSummary state = await _cpuPower.GetStateSummaryAsync();

            AdminChipText.Text = state.IsElevated ? "Administrateur" : "Droits admin requis";
            AdminChipText.Foreground = state.IsElevated
                ? FindResource("SuccessBrush") as Brush ?? AdminChipText.Foreground
                : FindResource("TextSecondaryBrush") as Brush ?? AdminChipText.Foreground;
            AdminChipIcon.Foreground = AdminChipText.Foreground;

            ActivePlanText.Text = $"Plan : {state.ActiveSchemeName}";

            Dictionary<CoclicoPowerMode, string> plans = await _cpuPower.FindCoclicoPlansAsync();
            UpdatePlanCard(CoclicoPowerMode.Economy, StatusEconomy, CardEconomy, plans, state);
            UpdatePlanCard(CoclicoPowerMode.Optimal, StatusOptimal, CardOptimal, plans, state);
            UpdatePlanCard(CoclicoPowerMode.Performance, StatusPerformance, CardPerformance, plans, state);

            UpdateAdaptiveUi();

            if (!state.IsElevated)
            {
                StatusText.Text = "Relance Coclico en tant qu'administrateur pour créer, activer ou tester les plans d'alimentation.";
            }
            else if (ServiceContainer.GetRequired<SettingsService>().Settings.AdaptivePowerEnabled && !_adaptive.IsEnabled)
            {
                (bool started, string message) = await _adaptive.StartAsync();
                if (!started)
                {
                    ToastService.ShowWarning(message);
                }

                UpdateAdaptiveUi();
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "PowerOptimizerView.LoadState");
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdatePlanCard(
        CoclicoPowerMode mode,
        TextBlock statusText,
        Border card,
        IReadOnlyDictionary<CoclicoPowerMode, string> plans,
        PowerStateSummary state)
    {
        if (!plans.TryGetValue(mode, out _))
        {
            statusText.Text = "Absent";
            statusText.Foreground = FindResource("TextMutedBrush") as Brush ?? statusText.Foreground;
            card.BorderBrush = FindResource("BorderSubtleBrush") as Brush ?? card.BorderBrush;
            return;
        }

        bool isActive = state.ActiveCoclicoMode == mode;
        statusText.Text = isActive ? "Actif" : "Prêt";
        statusText.Foreground = isActive
            ? FindResource("SuccessBrush") as Brush ?? statusText.Foreground
            : FindResource("TextSecondaryBrush") as Brush ?? statusText.Foreground;
        card.BorderBrush = isActive
            ? FindResource("PrimaryBrush") as Brush ?? card.BorderBrush
            : FindResource("BorderSubtleBrush") as Brush ?? card.BorderBrush;
    }

    private void UpdateCpuGauge()
    {
        double usage = _adaptive.SampleCpuUsage();
        CpuUsageBar.Value = usage;
        CpuUsageText.Text = $"{usage:0}%";
    }

    private void UpdateAdaptiveUi()
    {
        if (BtnToggleAdaptive == null)
        {
            return;
        }

        bool running = _adaptive.IsEnabled;
        BtnToggleAdaptive.Content = running ? "Arrêter" : "Activer";
        AdaptiveStatusText.Text = running ? _adaptive.Status : "Mode adaptatif inactif.";
    }

    private void OnAdaptiveStatusChanged(string status)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnAdaptiveStatusChanged(status));
            return;
        }

        AdaptiveStatusText.Text = status;
        UpdateAdaptiveUi();
    }

    private async void BtnCreatePlans_Click(object sender, RoutedEventArgs e)
    {
        if (!_cpuPower.IsElevated)
        {
            ToastService.ShowWarning("Les droits administrateur sont requis pour créer les plans Coclico.");
            return;
        }

        BtnCreatePlans.IsEnabled = false;
        StatusText.Text = "Création et configuration des plans Coclico en cours...";
        try
        {
            IProgress<string> progress = new Progress<string>(message => StatusText.Text = message);
            PowerPlanSetupReport report = await _cpuPower.EnsureCoclicoPlansAsync(progress);

            PlansReportText.Text = report.Success
                ? $"{report.AppliedSettings} paramètres appliqués, {report.SkippedSettings} ignorés (non supportés par ce PC)."
                : "Échec de la création des plans.";

            if (report.Success)
            {
                ToastService.Show("Plans Coclico prêts et auto-configurés.");
            }
            else
            {
                ToastService.ShowError("Impossible de créer tous les plans Coclico.");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "PowerOptimizerView.CreatePlans");
            ToastService.ShowError("Erreur pendant la configuration des plans.");
        }
        finally
        {
            BtnCreatePlans.IsEnabled = true;
            await LoadStateAsync();
        }
    }

    private async Task ActivateModeAsync(CoclicoPowerMode mode)
    {
        if (!_cpuPower.IsElevated)
        {
            ToastService.ShowWarning("Les droits administrateur sont requis pour changer de plan.");
            return;
        }

        try
        {
            if (_adaptive.IsEnabled)
            {
                await _adaptive.StopAsync();
                UpdateAdaptiveUi();
            }

            (bool ok, string message) = await _cpuPower.ActivatePlanAsync(mode);
            if (ok)
            {
                ToastService.Show(message);
            }
            else
            {
                ToastService.ShowError(message);
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, $"PowerOptimizerView.ActivateMode({mode})");
            ToastService.ShowError("Erreur pendant l'activation du plan.");
        }

        await LoadStateAsync();
    }

    private void BtnActivateEconomy_Click(object sender, RoutedEventArgs e) => _ = ActivateModeAsync(CoclicoPowerMode.Economy);

    private void BtnActivateOptimal_Click(object sender, RoutedEventArgs e) => _ = ActivateModeAsync(CoclicoPowerMode.Optimal);

    private void BtnActivatePerformance_Click(object sender, RoutedEventArgs e) => _ = ActivateModeAsync(CoclicoPowerMode.Performance);

    private async void BtnDeletePlans_Click(object sender, RoutedEventArgs e)
    {
        if (!_cpuPower.IsElevated)
        {
            ToastService.ShowWarning("Les droits administrateur sont requis pour supprimer les plans.");
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            "Supprimer les trois plans d'alimentation Coclico ? Le plan Équilibré de Windows sera réactivé.",
            "Suppression des plans Coclico",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (_adaptive.IsEnabled)
            {
                await _adaptive.StopAsync();
            }

            (bool ok, string message) = await _cpuPower.DeleteCoclicoPlansAsync();
            if (ok)
            {
                ToastService.Show(message);
            }
            else
            {
                ToastService.ShowError(message);
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "PowerOptimizerView.DeletePlans");
            ToastService.ShowError("Erreur pendant la suppression des plans.");
        }

        await LoadStateAsync();
    }

    private async void BtnToggleAdaptive_Click(object sender, RoutedEventArgs e)
    {
        BtnToggleAdaptive.IsEnabled = false;
        try
        {
            if (_adaptive.IsEnabled)
            {
                await _adaptive.StopAsync();
            }
            else
            {
                (bool started, string message) = await _adaptive.StartAsync();
                if (!started)
                {
                    ToastService.ShowError(message);
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "PowerOptimizerView.ToggleAdaptive");
            ToastService.ShowError("Erreur du mode adaptatif.");
        }
        finally
        {
            BtnToggleAdaptive.IsEnabled = true;
            UpdateAdaptiveUi();
        }
    }

    private async void BtnRunBenchmark_Click(object sender, RoutedEventArgs e)
    {
        if (_benchmarkRunning)
        {
            return;
        }

        if (!_cpuPower.IsElevated)
        {
            ToastService.ShowWarning("Les droits administrateur sont requis pour le test comparatif.");
            return;
        }

        _benchmarkRunning = true;
        BtnRunBenchmark.IsEnabled = false;
        BenchmarkResults.Children.Clear();
        BenchmarkProgress.Visibility = Visibility.Visible;
        BenchmarkProgress.Value = 0;

        try
        {
            await _cpuPower.EnsureCoclicoPlansAsync();
            PowerSchemeInfo? original = await _cpuPower.GetActiveSchemeAsync();

            _ = await Task.Run(() => CpuPowerBenchmark.Run(TimeSpan.FromMilliseconds(400), CancellationToken.None));

            CoclicoPowerMode[] modes = [CoclicoPowerMode.Economy, CoclicoPowerMode.Optimal, CoclicoPowerMode.Performance];
            List<(CoclicoPowerMode Mode, double Score)> results = [];
            double best = 0;

            for (int i = 0; i < modes.Length; i++)
            {
                CoclicoPowerMode mode = modes[i];
                StatusText.Text = $"Test du plan « {PowerPlanCatalog.GetPlanName(mode)} »...";
                BenchmarkProgress.Value = (100.0 * i) / modes.Length;

                (bool activated, _) = await _cpuPower.ActivatePlanAsync(mode);
                if (!activated)
                {
                    continue;
                }

                await Task.Delay(1500);
                double score = await Task.Run(() => CpuPowerBenchmark.Run(TimeSpan.FromSeconds(3), CancellationToken.None));
                results.Add((mode, score));
                best = Math.Max(best, score);
                AddBenchmarkRow(mode, score, best);
            }

            if (original != null)
            {
                StatusText.Text = "Restauration du plan de départ...";
                _ = await _cpuPower.ActivatePlanAsync(
                    PowerPlanCatalog.TryGetModeFromPlanName(original.Name) ?? CoclicoPowerMode.Optimal);
            }

            BenchmarkProgress.Value = 100;
            double top = results.Count == 0 ? 0 : results.Max(r => r.Score);
            foreach ((CoclicoPowerMode mode, double score) in results)
            {
                HighlightBestRow(mode, score, top);
            }

            StatusText.Text = results.Count == 0
                ? "Aucun plan testé."
                : $"Test terminé : meilleur plan « {PowerPlanCatalog.GetPlanName(results.First(r => Math.Abs(r.Score - top) < 0.01).Mode)} ».";
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "PowerOptimizerView.Benchmark");
            ToastService.ShowError("Erreur pendant le test comparatif.");
        }
        finally
        {
            _benchmarkRunning = false;
            BtnRunBenchmark.IsEnabled = true;
            BenchmarkProgress.Visibility = Visibility.Collapsed;
            await LoadStateAsync();
        }
    }

    private void AddBenchmarkRow(CoclicoPowerMode mode, double score, double currentBest)
    {
        var row = new Border
        {
            Background = FindResource("BgSurfaceBrush") as Brush ?? Brushes.Transparent,
            BorderBrush = FindResource("BorderSubtleBrush") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 6),
            Tag = mode
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = PowerPlanCatalog.GetPlanName(mode),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResource("TextPrimaryBrush") as Brush ?? Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(name, 0);
        _ = grid.Children.Add(name);

        var bar = new ProgressBar
        {
            Height = 6,
            Minimum = 0,
            Maximum = Math.Max(currentBest, 1),
            Value = score,
            BorderThickness = new Thickness(0),
            Background = FindResource("BgElevatedBrush") as Brush ?? Brushes.Gray,
            Foreground = FindResource("PrimaryBrush") as Brush ?? Brushes.Teal,
            Margin = new Thickness(16, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(bar, 1);
        _ = grid.Children.Add(bar);

        var scoreText = new TextBlock
        {
            Text = $"{score:0} Mo/s",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(scoreText, 2);
        _ = grid.Children.Add(scoreText);

        row.Child = grid;
        _ = BenchmarkResults.Children.Add(row);
    }

    private void HighlightBestRow(CoclicoPowerMode mode, double score, double top)
    {
        foreach (object child in BenchmarkResults.Children)
        {
            if (child is Border border
                && border.Tag is CoclicoPowerMode rowMode
                && rowMode == mode)
            {
                border.BorderBrush = Math.Abs(score - top) < 0.01
                    ? FindResource("SuccessBrush") as Brush ?? border.BorderBrush
                    : FindResource("BorderSubtleBrush") as Brush ?? border.BorderBrush;
            }
        }
    }
}