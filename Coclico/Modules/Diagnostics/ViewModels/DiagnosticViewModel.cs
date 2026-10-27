using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Services;
using Coclico.Services;
using CommunityToolkit.Mvvm.Input;

namespace Coclico.ViewModels;

public sealed class DiagnosticEvidenceRow
{
    public required string Name { get; init; }
    public required string Value { get; init; }
    public required string Source { get; init; }
    public required bool IsUnavailable { get; init; }
}

public sealed class DiagnosticFindingRow
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string CategoryText { get; init; }
    public required string SeverityText { get; init; }
    public required Brush SeverityBrush { get; init; }
    public required string ConfidenceText { get; init; }
    public required string Summary { get; init; }
    public required string Explanation { get; init; }
    public required string Causes { get; init; }
    public required string NextSteps { get; init; }
    public required IReadOnlyList<DiagnosticEvidenceRow> Evidence { get; init; }
    public required string ActionText { get; init; }
    public required string? ActionNavigationTag { get; init; }
    public bool HasAction => ActionNavigationTag is not null;
}

public sealed class DiagnosticCategoryCard
{
    public required DiagnosticCategory Category { get; init; }
    public required string Title { get; init; }
    public required string StateText { get; init; }
    public required Brush StateBrush { get; init; }
    public required string FindingsText { get; init; }
}

public sealed class DiagnosticHistoryRow
{
    public required Guid Id { get; init; }
    public required string DateText { get; init; }
    public required string ModeText { get; init; }
    public required string StatusText { get; init; }
    public required string FindingsText { get; init; }
}

public partial class DiagnosticViewModel : INotifyPropertyChanged, IDisposable
{
    private const string Subsystem = "DiagnosticViewModel";
    private static readonly TimeSpan MaxRunDuration = TimeSpan.FromMinutes(3);

    private static readonly Brush BrushOk = FreezeBrush(Color.FromRgb(34, 197, 94));
    private static readonly Brush BrushInfo = FreezeBrush(Color.FromRgb(59, 130, 246));
    private static readonly Brush BrushWarning = FreezeBrush(Color.FromRgb(234, 179, 8));
    private static readonly Brush BrushError = FreezeBrush(Color.FromRgb(239, 68, 68));
    private static readonly Brush BrushMuted = FreezeBrush(Color.FromRgb(120, 120, 140));

    private readonly IDiagnosticOrchestrator _orchestrator;
    private readonly IDiagnosticHistoryService _history;
    private readonly IDialogService _dialog;
    private CancellationTokenSource? _runCts;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<string>? ActionRequested;

    public ObservableCollection<DiagnosticFindingRow> Findings { get; } = [];

    public ObservableCollection<DiagnosticCategoryCard> CategoryCards { get; } = [];

    public ObservableCollection<DiagnosticHistoryRow> History { get; } = [];

    public DiagnosticRun? CurrentRun { get; private set; }

    public bool IsRunning { get; private set { if (field == value) return; field = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotRunning)); } }

    public bool IsNotRunning => !IsRunning;

    public string StatusText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = L("Diag_State_None");

    public string ProgressText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = string.Empty;

    public string LastRunText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = string.Empty;

    public string SummaryText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = string.Empty;

    public bool HasResult { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } }

    public bool HasHistory => History.Count > 0;

    public bool SelectWindows { get; set { if (field == value) return; field = value; OnPropertyChanged(); } }
    public bool SelectCpu { get; set { if (field == value) return; field = value; OnPropertyChanged(); } }
    public bool SelectGpu { get; set { if (field == value) return; field = value; OnPropertyChanged(); } }
    public bool SelectMemory { get; set { if (field == value) return; field = value; OnPropertyChanged(); } }
    public bool SelectStorage { get; set { if (field == value) return; field = value; OnPropertyChanged(); } }
    public bool SelectNetwork { get; set { if (field == value) return; field = value; OnPropertyChanged(); } }
    public bool SelectPower { get; set { if (field == value) return; field = value; OnPropertyChanged(); } }
    public bool SelectDevices { get; set { if (field == value) return; field = value; OnPropertyChanged(); } }
    public bool SelectProcesses { get; set { if (field == value) return; field = value; OnPropertyChanged(); } }
    public bool SelectSecurity { get; set { if (field == value) return; field = value; OnPropertyChanged(); } }
    public bool SelectMotherboard { get; set { if (field == value) return; field = value; OnPropertyChanged(); } }

    public DiagnosticViewModel(
        IDiagnosticOrchestrator orchestrator,
        IDiagnosticHistoryService history,
        IDialogService dialog)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));

        RebuildCategoryCards(null);
    }

    [RelayCommand]
    private Task RunQuickAsync() => RunDiagnosticAsync(DiagnosticRequest.Quick());

    [RelayCommand]
    private Task RunFullAsync() => RunDiagnosticAsync(DiagnosticRequest.Full());

    [RelayCommand]
    private Task RunTargetedAsync()
    {
        List<DiagnosticCategory> selected = [];
        if (SelectWindows) selected.Add(DiagnosticCategory.Windows);
        if (SelectCpu) selected.Add(DiagnosticCategory.Cpu);
        if (SelectGpu) selected.Add(DiagnosticCategory.Gpu);
        if (SelectMemory) selected.Add(DiagnosticCategory.Memory);
        if (SelectStorage) selected.Add(DiagnosticCategory.Storage);
        if (SelectNetwork) selected.Add(DiagnosticCategory.Network);
        if (SelectPower) selected.Add(DiagnosticCategory.Power);
        if (SelectDevices) selected.Add(DiagnosticCategory.Devices);
        if (SelectProcesses) selected.Add(DiagnosticCategory.Processes);
        if (SelectSecurity) selected.Add(DiagnosticCategory.Security);
        if (SelectMotherboard) selected.Add(DiagnosticCategory.Motherboard);

        if (selected.Count == 0)
        {
            StatusText = L("Diag_State_NoCategory");
            return Task.CompletedTask;
        }

        return RunDiagnosticAsync(DiagnosticRequest.Targeted(selected));
    }

    [RelayCommand]
    private void Cancel()
    {
        try
        {
            _runCts?.Cancel();
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, $"{Subsystem}.Cancel");
        }
    }

    [RelayCommand]
    private Task ExportJsonAsync() => ExportAsync(asText: false);

    [RelayCommand]
    private Task ExportTextAsync() => ExportAsync(asText: true);

    [RelayCommand]
    private Task RefreshHistoryAsync() => LoadHistoryAsync();

    [RelayCommand]
    private async Task DeleteHistoryAsync()
    {
        try
        {
            if (CurrentRun is not null)
            {
                await _history.DeleteAsync(CurrentRun.Id, CancellationToken.None).ConfigureAwait(false);
            }

            foreach (DiagnosticHistoryRow row in History.ToList())
            {
                await _history.DeleteAsync(row.Id, CancellationToken.None).ConfigureAwait(false);
            }

            History.Clear();
            OnPropertyChanged(nameof(HasHistory));
            StatusText = L("Diag_State_HistoryCleared");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoggingService.LogException(ex, $"{Subsystem}.DeleteHistory");
        }
    }

    private async Task RunDiagnosticAsync(DiagnosticRequest request)
    {
        if (IsRunning)
        {
            return;
        }

        _runCts = new CancellationTokenSource(MaxRunDuration);
        CancellationTokenSource cts = _runCts;
        IsRunning = true;
        StatusText = L("Diag_State_Running");
        ProgressText = string.Empty;

        Progress<DiagnosticProgress> progress = new(OnProgressReported);

        try
        {
            DiagnosticRun run = await Task.Run(
                () => _orchestrator.RunAsync(request, progress, cts.Token),
                CancellationToken.None).ConfigureAwait(true);

            CurrentRun = run;
            PopulateResults(run);
            StatusText = run.Status switch
            {
                DiagnosticRunStatus.Cancelled => L("Diag_State_Interrupted"),
                DiagnosticRunStatus.Partial => L("Diag_State_Partial"),
                _ => L("Diag_State_Results")
            };
        }
        catch (OperationCanceledException)
        {
            StatusText = L("Diag_State_Interrupted");
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, $"{Subsystem}.Run");
            StatusText = L("Diag_State_GlobalError");
        }
        finally
        {
            IsRunning = false;
            _runCts = null;
            await LoadHistoryAsync();
        }
    }

    private void OnProgressReported(DiagnosticProgress value)
        => RunOnUi(() => ProgressText = L("Diag_ProgressFormat")
            .Replace("{done}", value.CompletedChecks.ToString(System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal)
            .Replace("{total}", value.TotalChecks.ToString(System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal)
            .Replace("{check}", L($"Diag_Check_{value.CurrentCheckId.Replace('.', '_')}"), StringComparison.Ordinal));

    private void PopulateResults(DiagnosticRun run)
    {
        Findings.Clear();
        foreach (DiagnosticFinding finding in run.Findings)
        {
            Findings.Add(ToRow(finding));
        }

        RebuildCategoryCards(run);

        SummaryText = run.Summary is DiagnosticSummary summary
            ? L("Diag_SummaryFormat")
                .Replace("{completed}", summary.ChecksCompleted.ToString(System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal)
                .Replace("{total}", summary.ChecksTotal.ToString(System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal)
                .Replace("{failed}", summary.ChecksFailed.ToString(System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal)
                .Replace("{warning}", summary.FindingsWarning.ToString(System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal)
                .Replace("{error}", summary.FindingsError.ToString(System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal)
                .Replace("{critical}", summary.FindingsCritical.ToString(System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal)
                + (run.Findings.Count == 0 ? " " + L("Diag_SummaryNoFinding") : string.Empty)
            : string.Empty;

        LastRunText = run.StartedAtUtc.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
        HasResult = true;
    }

    private DiagnosticFindingRow ToRow(DiagnosticFinding finding)
    {
        DiagnosticActionDescriptor? action = null;
        foreach (string actionId in finding.ActionIds)
        {
            if (DiagnosticActionCatalog.TryGet(actionId, out DiagnosticActionDescriptor? descriptor)
                && descriptor.NavigationTag is not null)
            {
                action = descriptor;
                break;
            }
        }

        return new DiagnosticFindingRow
        {
            Id = finding.Id,
            Title = L(finding.TitleKey),
            CategoryText = L($"Diag_Category_{finding.Category}"),
            SeverityText = L($"Diag_Severity_{finding.Severity}"),
            SeverityBrush = BrushFor(finding.Severity),
            ConfidenceText = L($"Diag_Confidence_{finding.Confidence}"),
            Summary = string.IsNullOrEmpty(finding.SummaryKey) ? string.Empty : L(finding.SummaryKey),
            Explanation = string.IsNullOrEmpty(finding.ExplanationKey) ? string.Empty : L(finding.ExplanationKey),
            Causes = string.Join(" · ", finding.PossibleCauseKeys.Select(L)),
            NextSteps = string.Join(" · ", finding.RecommendedNextStepKeys.Select(L)),
            Evidence = [.. finding.Evidence.Select(e => new DiagnosticEvidenceRow
            {
                Name = L(e.DisplayNameKey),
                Value = FormatEvidence(e),
                Source = e.Source,
                IsUnavailable = e.State != DiagnosticValueState.Measured
            })],
            ActionText = action is null ? string.Empty : L(action.TitleKey),
            ActionNavigationTag = action?.NavigationTag
        };
    }

    private static string FormatEvidence(DiagnosticEvidence evidence)
    {
        if (evidence.State != DiagnosticValueState.Measured)
        {
            return L($"Diag_ValueState_{evidence.State}");
        }

        string value = evidence.Value ?? "-";
        if (evidence.Unit == "bytes" && long.TryParse(value, out long bytes))
        {
            return BytesText(bytes);
        }

        return string.IsNullOrEmpty(evidence.Unit) ? value : $"{value} {evidence.Unit}";
    }

    private static string BytesText(long bytes)
        => bytes >= 1024 * 1024 * 1024 ? $"{bytes / (1024.0 * 1024 * 1024):F1} GiB"
         : bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024):F1} MiB"
         : bytes >= 1024 ? $"{bytes / 1024.0:F1} KiB"
         : $"{bytes} B";

    private void RebuildCategoryCards(DiagnosticRun? run)
    {
        CategoryCards.Clear();
        foreach (DiagnosticCategory category in Enum.GetValues<DiagnosticCategory>())
        {
            List<DiagnosticCheckResult> checks = run?.Checks
                .Where(c => c.Category == category)
                .ToList() ?? [];

            string stateText;
            Brush stateBrush;
            string findingsText;

            if (checks.Count == 0)
            {
                stateText = L("Diag_ValueState_NotExposed");
                stateBrush = BrushMuted;
                findingsText = L("Diag_State_NoData");
            }
            else if (checks.All(c => c.Status is DiagnosticCheckStatus.Failed or DiagnosticCheckStatus.TimedOut))
            {
                stateText = L("Diag_State_CheckFailed");
                stateBrush = BrushMuted;
                findingsText = L("Diag_State_NoData");
            }
            else
            {
                List<DiagnosticFinding> findings = [.. run!.Findings.Where(f => f.Category == category)];
                DiagnosticSeverity worst = findings.Count == 0
                    ? DiagnosticSeverity.Success
                    : findings.Max(f => f.Severity);

                (stateText, stateBrush) = worst switch
                {
                    DiagnosticSeverity.Critical or DiagnosticSeverity.Error => (L($"Diag_Severity_{worst}"), BrushError),
                    DiagnosticSeverity.Warning => (L($"Diag_Severity_{worst}"), BrushWarning),
                    DiagnosticSeverity.Info => (L($"Diag_Severity_{worst}"), BrushInfo),
                    _ => (L("Diag_State_Passed"), BrushOk)
                };
                findingsText = findings.Count == 0
                    ? L("Diag_Card_NoFinding")
                    : TextFormatService.Format(System.Globalization.CultureInfo.CurrentCulture, L("Diag_Card_FindingsFormat"), findings.Count);
            }

            CategoryCards.Add(new DiagnosticCategoryCard
            {
                Category = category,
                Title = L($"Diag_Category_{category}"),
                StateText = stateText,
                StateBrush = stateBrush,
                FindingsText = findingsText
            });
        }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            IReadOnlyList<DiagnosticRunSummary> summaries = await _history
                .GetRecentAsync(20, CancellationToken.None)
                .ConfigureAwait(true);

            History.Clear();
            foreach (DiagnosticRunSummary summary in summaries)
            {
                History.Add(new DiagnosticHistoryRow
                {
                    Id = summary.Id,
                    DateText = summary.StartedAtUtc.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture),
                    ModeText = L($"Diag_Mode_{summary.Mode}"),
                    StatusText = L($"Diag_RunStatus_{summary.Status}"),
                    FindingsText = TextFormatService.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        L("Diag_Card_FindingsFormat"),
                        summary.FindingsWarning + summary.FindingsError + summary.FindingsCritical)
                });
            }

            OnPropertyChanged(nameof(HasHistory));
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, $"{Subsystem}.LoadHistory");
        }
    }

    private async Task ExportAsync(bool asText)
    {
        DiagnosticRun? run = CurrentRun;
        if (run is null)
        {
            return;
        }

        try
        {
            string filter = asText
                ? "Text report (*.txt)|*.txt"
                : "JSON report (*.json)|*.json";

            string? target = _dialog.ShowSaveFileDialog(
                L(asText ? "Diag_ExportTextTitle" : "Diag_ExportJsonTitle"),
                filter,
                asText ? ".txt" : ".json");

            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            string content = asText
                ? DiagnosticReportExporter.ExportText(run)
                : DiagnosticReportExporter.ExportJson(run);
            await File.WriteAllTextAsync(target, content, CancellationToken.None).ConfigureAwait(false);
            StatusText = L("Diag_State_Exported");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.InvalidOperationException)
        {
            LoggingService.LogException(ex, $"{Subsystem}.Export");
            StatusText = L("Diag_State_ExportFailed");
        }
    }

    public void InvokeAction(DiagnosticFindingRow row)
    {
        if (row.ActionNavigationTag is string tag)
        {
            ActionRequested?.Invoke(tag);
        }
    }

    public void OnViewLoaded()
    {
        _ = LoadHistoryAsync();
    }

    private static Brush BrushFor(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Critical or DiagnosticSeverity.Error => BrushError,
        DiagnosticSeverity.Warning => BrushWarning,
        DiagnosticSeverity.Info => BrushInfo,
        _ => BrushOk
    };

    private static Brush FreezeBrush(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }

    private static string L(string key)
    {
        try
        {
            return Application.Current?.TryFindResource(key) as string ?? key;
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            return key;
        }
    }

    private void RunOnUi(Action action)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _ = dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _runCts?.Cancel();
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, $"{Subsystem}.Dispose");
        }

        _runCts = null;
    }
}
