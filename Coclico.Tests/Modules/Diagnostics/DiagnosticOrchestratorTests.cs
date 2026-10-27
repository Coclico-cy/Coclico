using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class DiagnosticOrchestratorTests
{
    private sealed record FakeCheck(
        string Id,
        DiagnosticCategory Category,
        bool IsQuick,
        bool Supported = true,
        TimeSpan? Latency = null,
        Exception? Throw = null,
        bool IgnoreCancellation = false,
        IReadOnlyList<DiagnosticFinding>? Findings = null) : IDiagnosticCheck
    {
        public int RunCount;

        public bool IsSupported(DiagnosticEnvironment environment) => Supported;

        public async Task<DiagnosticCheckResult> RunAsync(DiagnosticContext context)
        {
            RunCount++;
            if (Throw is not null)
            {
                throw Throw;
            }

            if (Latency is TimeSpan latency)
            {
                await Task.Delay(latency, IgnoreCancellation
                    ? CancellationToken.None
                    : context.CancellationToken).ConfigureAwait(false);
            }

            return DiagnosticCheckResult.Finished(
                Id, Category, IsQuick,
                Findings is { Count: > 0 } ? DiagnosticCheckStatus.Findings : DiagnosticCheckStatus.Passed,
                Findings ?? [],
                TimeSpan.Zero);
        }
    }

    private sealed class FakeHistory : IDiagnosticHistoryService
    {
        public List<DiagnosticRun> Saved { get; } = [];

        public Task SaveAsync(DiagnosticRun run, CancellationToken cancellationToken)
        {
            Saved.Add(run);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DiagnosticRunSummary>> GetRecentAsync(
            int limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DiagnosticRunSummary>>([]);

        public Task<DiagnosticRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult<DiagnosticRun?>(null);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class CollectingProgress : IProgress<DiagnosticProgress>
    {
        private readonly object _gate = new();

        public List<DiagnosticProgress> Reports { get; } = [];

        public void Report(DiagnosticProgress value)
        {
            lock (_gate)
            {
                Reports.Add(value);
            }
        }
    }

    private static DiagnosticOrchestrator CreateOrchestrator(
        FakeHistory history, params IDiagnosticCheck[] checks)
        => new(checks, history, new DiagnosticPrivacySanitizer());

    [Fact]
    public async Task QuickMode_RunsOnlyQuickChecks()
    {
        FakeCheck quick = new("windows.health", DiagnosticCategory.Windows, IsQuick: true);
        FakeCheck slow = new("motherboard.info", DiagnosticCategory.Motherboard, IsQuick: false);
        FakeHistory history = new();

        DiagnosticRun run = await CreateOrchestrator(history, quick, slow)
            .RunAsync(DiagnosticRequest.Quick(), null, CancellationToken.None);

        Assert.Equal(1, quick.RunCount);
        Assert.Equal(0, slow.RunCount);
        Assert.Single(run.Checks);
        Assert.Equal(DiagnosticRunStatus.Completed, run.Status);
    }

    [Fact]
    public async Task FullMode_RunsAllSupportedChecks()
    {
        FakeCheck a = new("cpu.info", DiagnosticCategory.Cpu, IsQuick: true);
        FakeCheck b = new("gpu.info", DiagnosticCategory.Gpu, IsQuick: true);
        FakeCheck c = new("motherboard.info", DiagnosticCategory.Motherboard, IsQuick: false);
        FakeCheck unsupported = new("x.unsupported", DiagnosticCategory.Devices, IsQuick: false, Supported: false);
        FakeHistory history = new();

        DiagnosticRun run = await CreateOrchestrator(history, a, b, c, unsupported)
            .RunAsync(DiagnosticRequest.Full(), null, CancellationToken.None);

        Assert.Equal(3, run.Checks.Count);
        Assert.DoesNotContain(run.Checks, r => r.CheckId == "x.unsupported");
        Assert.Equal(DiagnosticRunStatus.Completed, run.Status);
    }

    [Fact]
    public async Task TargetedMode_RunsOnlySelectedCategories()
    {
        FakeCheck cpu = new("cpu.info", DiagnosticCategory.Cpu, IsQuick: true);
        FakeCheck network = new("network.probe", DiagnosticCategory.Network, IsQuick: true);
        FakeCheck storage = new("storage.space", DiagnosticCategory.Storage, IsQuick: true);
        FakeHistory history = new();

        DiagnosticRun run = await CreateOrchestrator(history, cpu, network, storage)
            .RunAsync(
                DiagnosticRequest.Targeted([DiagnosticCategory.Network]),
                null,
                CancellationToken.None);

        Assert.Equal(0, cpu.RunCount);
        Assert.Equal(1, network.RunCount);
        Assert.Equal(0, storage.RunCount);
        Assert.Single(run.Checks);
        Assert.Equal(DiagnosticMode.Targeted, run.Mode);
    }

    [Fact]
    public async Task OneFailingCheck_DoesNotFailTheRun()
    {
        FakeCheck ok = new("cpu.info", DiagnosticCategory.Cpu, IsQuick: true);
        FakeCheck broken = new("gpu.info", DiagnosticCategory.Gpu, IsQuick: true,
            Throw: new InvalidOperationException("WMI exploded"));
        FakeHistory history = new();

        DiagnosticRun run = await CreateOrchestrator(history, ok, broken)
            .RunAsync(DiagnosticRequest.Full(), null, CancellationToken.None);

        Assert.Equal(DiagnosticRunStatus.Partial, run.Status);
        Assert.Equal(2, run.Checks.Count);
        Assert.Equal(DiagnosticCheckStatus.Failed, run.Checks.First(r => r.CheckId == "gpu.info").Status);
        Assert.Equal(DiagnosticCheckStatus.Passed, run.Checks.First(r => r.CheckId == "cpu.info").Status);
    }

    [Fact]
    public async Task Timeout_ProducesTimedOutAndOthersContinue()
    {
        FakeCheck slow = new("network.probe", DiagnosticCategory.Network, IsQuick: true,
            Latency: TimeSpan.FromSeconds(30));
        FakeCheck fast = new("cpu.info", DiagnosticCategory.Cpu, IsQuick: true);
        FakeHistory history = new();

        DiagnosticRequest request = new()
        {
            Mode = DiagnosticMode.Full,
            PerCheckTimeout = TimeSpan.FromMilliseconds(150)
        };

        DiagnosticRun run = await CreateOrchestrator(history, slow, fast)
            .RunAsync(request, null, CancellationToken.None);

        Assert.Equal(DiagnosticCheckStatus.TimedOut,
            run.Checks.First(r => r.CheckId == "network.probe").Status);
        Assert.Equal(DiagnosticCheckStatus.Passed,
            run.Checks.First(r => r.CheckId == "cpu.info").Status);
        Assert.Equal(DiagnosticRunStatus.Partial, run.Status);
    }

    [Fact]
    public async Task UserCancellation_StopsAndReportsCancelled()
    {
        FakeCheck slow = new("network.probe", DiagnosticCategory.Network, IsQuick: true,
            Latency: TimeSpan.FromSeconds(30));
        FakeCheck fast = new("cpu.info", DiagnosticCategory.Cpu, IsQuick: true);
        FakeHistory history = new();

        using CancellationTokenSource cts = new();
        DiagnosticRequest request = new() { Mode = DiagnosticMode.Full };

        Task<DiagnosticRun> runTask = CreateOrchestrator(history, slow, fast)
            .RunAsync(request, null, cts.Token);
        await Task.Delay(300, CancellationToken.None);
        cts.Cancel();

        DiagnosticRun run = await runTask;

        Assert.Equal(DiagnosticRunStatus.Cancelled, run.Status);
    }

    [Fact]
    public async Task Progress_ReportsEveryCompletedCheck()
    {
        FakeCheck a = new("a.check", DiagnosticCategory.Cpu, IsQuick: true);
        FakeCheck b = new("b.check", DiagnosticCategory.Memory, IsQuick: true);
        FakeCheck c = new("c.check", DiagnosticCategory.Storage, IsQuick: true);
        FakeHistory history = new();
        CollectingProgress progress = new();

        DiagnosticRun run = await CreateOrchestrator(history, a, b, c)
            .RunAsync(DiagnosticRequest.Quick(), progress, CancellationToken.None);

        List<DiagnosticProgress> reports = progress.Reports;
        Assert.Equal(3, run.Checks.Count);
        Assert.Equal(3, reports.Count);
        Assert.All(reports, r => Assert.Equal(3, r.TotalChecks));
        Assert.Equal(3, reports.Max(r => r.CompletedChecks));
        Assert.Equal(1, reports.Min(r => r.CompletedChecks));
    }

    [Fact]
    public async Task Run_SanitizesDeduplicatesAndSortsFindings()
    {
        DiagnosticFinding MakeFinding(string id, string? entity, DiagnosticSeverity severity) => new()
        {
            Id = id,
            Entity = entity,
            Category = DiagnosticCategory.Storage,
            Severity = severity,
            TitleKey = "Diag_Finding_LowSpace_Title",
            ObservedAtUtc = DateTime.UtcNow,
            Evidence = [DiagnosticEvidence.Measured("test.path", "Diag_Evidence_TotalSize", "C:\\Users\\martin\\file.txt", "", "DriveInfo")]
        };

        FakeCheck withFindings = new("storage.space", DiagnosticCategory.Storage, IsQuick: true,
            Findings:
            [
                MakeFinding("STORAGE_LOW_SPACE_001", "C:", DiagnosticSeverity.Warning),
                MakeFinding("STORAGE_LOW_SPACE_001", "C:", DiagnosticSeverity.Error),
                MakeFinding("OTHER_001", null, DiagnosticSeverity.Info)
            ]);

        FakeHistory history = new();
        DiagnosticRun run = await CreateOrchestrator(history, withFindings)
            .RunAsync(DiagnosticRequest.Quick(), null, CancellationToken.None);

        Assert.Equal(2, run.Findings.Count);
        Assert.Equal(DiagnosticSeverity.Error, run.Findings[0].Severity);
        Assert.All(run.Findings[0].Evidence,
            e => Assert.DoesNotContain("martin", e.Value ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_SavesToHistoryEvenWhenPartial()
    {
        FakeCheck broken = new("gpu.info", DiagnosticCategory.Gpu, IsQuick: true,
            Throw: new InvalidOperationException("boom"));
        FakeHistory history = new();

        DiagnosticRun run = await CreateOrchestrator(history, broken)
            .RunAsync(DiagnosticRequest.Quick(), null, CancellationToken.None);

        Assert.Equal(DiagnosticRunStatus.Partial, run.Status);
        DiagnosticRun saved = Assert.Single(history.Saved);
        Assert.Equal(run.Id, saved.Id);
    }

    [Fact]
    public async Task Run_WithNoSelectedChecks_SavesNothing()
    {
        FakeHistory history = new();
        IDiagnosticCheck check = new FakeCheck("cpu.info", DiagnosticCategory.Cpu, IsQuick: false);

        DiagnosticRun run = await CreateOrchestrator(history, check)
            .RunAsync(DiagnosticRequest.Quick(), null, CancellationToken.None);

        Assert.Empty(run.Checks);
        Assert.Empty(history.Saved);
    }
}
