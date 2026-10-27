namespace Coclico.Modules.Diagnostics.Services;

using System.IO;
using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Rules;
using Coclico.Services;

public interface IDiagnosticOrchestrator
{
    Task<DiagnosticRun> RunAsync(
        DiagnosticRequest request,
        IProgress<DiagnosticProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class DiagnosticOrchestrator : IDiagnosticOrchestrator
{
    private const string Subsystem = "DiagnosticOrchestrator";

    private readonly IReadOnlyList<IDiagnosticCheck> _checks;
    private readonly IDiagnosticHistoryService _history;
    private readonly DiagnosticPrivacySanitizer _sanitizer;

    public DiagnosticOrchestrator(
        IEnumerable<IDiagnosticCheck> checks,
        IDiagnosticHistoryService history,
        DiagnosticPrivacySanitizer sanitizer)
    {
        _checks = [.. checks];
        _history = history;
        _sanitizer = sanitizer;
    }

    public async Task<DiagnosticRun> RunAsync(
        DiagnosticRequest request,
        IProgress<DiagnosticProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DiagnosticEnvironment environment = BuildEnvironment();
        DiagnosticRun run = new()
        {
            Mode = request.Mode,
            ApplicationVersion = TryGetApplicationVersion() ?? "unknown",
            RulesVersion = DiagnosticRuleEngine.RulesVersion,
            SystemSnapshot = BuildSnapshot(environment)
        };

        List<IDiagnosticCheck> selected = SelectChecks(request, environment);
        int totalChecks = selected.Count;
        int completed = 0;

        int maxParallel = Math.Max(1, Math.Min(request.MaxParallelChecks, 4));
        using SemaphoreSlim gate = new(maxParallel, maxParallel);

        List<DiagnosticCheckResult> results = [];
        object resultLock = new();

        try
        {
            await Parallel.ForEachAsync(
                selected,
                cancellationToken,
                async (check, ct) =>
                {
                    DiagnosticCheckResult result = await RunOneAsync(check, request, environment, gate, ct).ConfigureAwait(false);

                    int order;
                    lock (resultLock)
                    {
                        results.Add(result);
                        completed++;
                        order = completed;
                    }

                    progress?.Report(new DiagnosticProgress(check.Id, check.Category, order, totalChecks));
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run.Status = DiagnosticRunStatus.Cancelled;
        }

        if (run.Status != DiagnosticRunStatus.Cancelled
            && cancellationToken.IsCancellationRequested
            && results.Count < totalChecks)
        {
            run.Status = DiagnosticRunStatus.Cancelled;
        }

        List<DiagnosticFinding> findings = [.. _sanitizer.SanitizeFindings(
            DiagnosticRuleEngine.Deduplicate(results.SelectMany(r => r.Findings)))];

        run.Findings = [.. DiagnosticRuleEngine.SortFindings(findings)];
        run.Checks = [.. results.OrderBy(r => r.Category).ThenBy(r => r.CheckId, StringComparer.Ordinal)];
        run.CompletedAtUtc = DateTime.UtcNow;
        run.Summary = DiagnosticSummaryBuilder.Build(run);

        if (run.Status != DiagnosticRunStatus.Cancelled)
        {
            run.Status = run.Checks.Any(c => c.Status is DiagnosticCheckStatus.Failed or DiagnosticCheckStatus.TimedOut)
                ? DiagnosticRunStatus.Partial
                : DiagnosticRunStatus.Completed;
        }

        if (run.Checks.Count > 0)
        {
            try
            {
                await _history.SaveAsync(run, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) { _ = ex; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LoggingService.LogException(ex, $"{Subsystem}.Save");
            }
        }

        return run;
    }

    private async Task<DiagnosticCheckResult> RunOneAsync(
        IDiagnosticCheck check,
        DiagnosticRequest request,
        DiagnosticEnvironment environment,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTime start = DateTime.UtcNow;
            DiagnosticCheckResult result;

            try
            {
                result = await RunWithTimeoutAsync(check, environment, request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result = new DiagnosticCheckResult
                {
                    CheckId = check.Id,
                    Category = check.Category,
                    IsQuick = check.IsQuick,
                    Status = DiagnosticCheckStatus.Cancelled
                };
            }
            catch (OperationCanceledException)
            {
                result = DiagnosticCheckResult.Finished(
                    check.Id, check.Category, check.IsQuick,
                    DiagnosticCheckStatus.TimedOut, [], TimeSpan.Zero,
                    reason: "check_timeout");
            }
            catch (Exception ex) when (ex is not System.OutOfMemoryException)
            {
                LoggingService.LogException(ex, $"{Subsystem}.{check.Id}");
                result = DiagnosticCheckResult.Finished(
                    check.Id, check.Category, check.IsQuick,
                    DiagnosticCheckStatus.Failed, [], TimeSpan.Zero,
                    reason: "check_failed");
            }

            return result with { Duration = DateTime.UtcNow - start };
        }
        finally
        {
            _ = gate.Release();
        }
    }

    private static async Task<DiagnosticCheckResult> RunWithTimeoutAsync(
        IDiagnosticCheck check,
        DiagnosticEnvironment environment,
        DiagnosticRequest request,
        CancellationToken outerToken)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        Task<DiagnosticCheckResult> run = Task.Run(
            () => check.RunAsync(new DiagnosticContext
            {
                Environment = environment,
                Request = request,
                CancellationToken = timeoutCts.Token
            }),
            CancellationToken.None);

        Task timeout = Task.Delay(request.PerCheckTimeout, CancellationToken.None);
        Task finished = await Task.WhenAny(run, timeout).ConfigureAwait(false);

        if (finished == run)
        {
            return await run.ConfigureAwait(false);
        }

        await timeoutCts.CancelAsync().ConfigureAwait(false);
        Task graceEnd = Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
        Task done = await Task.WhenAny(run, graceEnd).ConfigureAwait(false);

        return done == run
            ? await run.ConfigureAwait(false)
            : DiagnosticCheckResult.Finished(
                check.Id, check.Category, check.IsQuick,
                DiagnosticCheckStatus.TimedOut, [], TimeSpan.Zero,
                reason: "check_timeout");
    }

    private List<IDiagnosticCheck> SelectChecks(DiagnosticRequest request, DiagnosticEnvironment environment)
    {
        IEnumerable<IDiagnosticCheck> candidates = request.Mode switch
        {
            DiagnosticMode.Quick => _checks.Where(c => c.IsQuick),
            DiagnosticMode.Full => _checks,
            DiagnosticMode.Targeted => _checks.Where(c => request.Categories.Contains(c.Category)),
            _ => []
        };

        return [.. candidates.Where(c => c.IsSupported(environment))];
    }

    private static DiagnosticEnvironment BuildEnvironment()
    {
        bool hasBattery = false;
        try
        {
            hasBattery = NativeDiag.HasBattery();
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, $"{Subsystem}.BuildEnvironment");
        }

        return new DiagnosticEnvironment(
            IsElevated: App.IsRunningAsAdministrator(),
            HasBattery: hasBattery,
            HardwareSensorsEnabled: true,
            OsVersion: Environment.OSVersion.VersionString);
    }

    private static DiagnosticSystemSnapshot? BuildSnapshot(DiagnosticEnvironment environment)
    {
        try
        {
            MemoryCleanerService.RamInfo ram = MemoryCleanerService.GetRamInfo();
            return new DiagnosticSystemSnapshot(
                environment.OsVersion,
                Environment.Is64BitOperatingSystem ? "x64" : "x86",
                Environment.ProcessorCount,
                ram.TotalPhysBytes,
                environment.IsElevated);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            return null;
        }
    }

    private static string? TryGetApplicationVersion()
    {
        try
        {
            return System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString();
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            return null;
        }
    }
}
