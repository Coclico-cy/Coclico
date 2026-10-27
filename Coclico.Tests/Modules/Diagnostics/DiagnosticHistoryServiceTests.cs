using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class DiagnosticHistoryServiceTests : IDisposable
{
    private readonly string _baseDirectory;

    public DiagnosticHistoryServiceTests()
    {
        _baseDirectory = Path.Join(Path.GetTempPath(), "coclico-diag-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_baseDirectory, recursive: true);
        }
        catch (DirectoryNotFoundException ex) { _ = ex; }
    }

    private DiagnosticHistoryService CreateService(int retentionCount = 30, int retentionDays = 30)
        => new(_baseDirectory, retentionCount, retentionDays);

    private static DiagnosticRun MakeRun(DateTime? startedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        StartedAtUtc = startedAt ?? DateTime.UtcNow,
        Mode = DiagnosticMode.Quick,
        Status = DiagnosticRunStatus.Completed,
        ApplicationVersion = "1.0.0",
        RulesVersion = "1.0",
        SchemaVersion = 1,
        CompletedAtUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task SaveThenGet_RoundTripsTheRun()
    {
        DiagnosticHistoryService service = CreateService();
        DiagnosticRun run = MakeRun();
        run.Findings.Add(new DiagnosticFinding
        {
            Id = "STORAGE_LOW_SPACE_001",
            Entity = "C:",
            Category = DiagnosticCategory.Storage,
            Severity = DiagnosticSeverity.Warning,
            Confidence = DiagnosticConfidence.High,
            TitleKey = "Diag_Finding_LowSpace_Title",
            ObservedAtUtc = DateTime.UtcNow
        });

        await service.SaveAsync(run, CancellationToken.None);

        DiagnosticRun? loaded = await service.GetByIdAsync(run.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(run.Id, loaded.Id);
        Assert.Equal(run.Mode, loaded.Mode);
        Assert.Equal(run.Status, loaded.Status);
        Assert.Equal(run.RulesVersion, loaded.RulesVersion);
        DiagnosticFinding finding = Assert.Single(loaded.Findings);
        Assert.Equal("STORAGE_LOW_SPACE_001", finding.Id);
        Assert.Equal("C:", finding.Entity);
    }

    [Fact]
    public async Task GetById_UnknownId_ReturnsNull()
    {
        DiagnosticHistoryService service = CreateService();
        Assert.Null(await service.GetByIdAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesTheRun()
    {
        DiagnosticHistoryService service = CreateService();
        DiagnosticRun run = MakeRun();
        await service.SaveAsync(run, CancellationToken.None);

        await service.DeleteAsync(run.Id, CancellationToken.None);

        Assert.Null(await service.GetByIdAsync(run.Id, CancellationToken.None));
        Assert.Empty(await service.GetRecentAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task GetRecent_OrdersByDateDescending()
    {
        DiagnosticHistoryService service = CreateService();
        DateTime baseTime = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        await service.SaveAsync(MakeRun(baseTime.AddDays(1)), CancellationToken.None);
        await service.SaveAsync(MakeRun(baseTime.AddDays(3)), CancellationToken.None);
        await service.SaveAsync(MakeRun(baseTime.AddDays(2)), CancellationToken.None);

        IReadOnlyList<DiagnosticRunSummary> recent = await service.GetRecentAsync(10, CancellationToken.None);

        Assert.Equal(3, recent.Count);
        Assert.Equal(baseTime.AddDays(3), recent[0].StartedAtUtc);
        Assert.Equal(baseTime.AddDays(2), recent[1].StartedAtUtc);
        Assert.Equal(baseTime.AddDays(1), recent[2].StartedAtUtc);
    }

    [Fact]
    public async Task Retention_ByCount_KeepsOnlyMostRecent()
    {
        DiagnosticHistoryService service = CreateService(retentionCount: 3, retentionDays: 30);
        DateTime baseTime = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int day = 1; day <= 5; day++)
        {
            await service.SaveAsync(MakeRun(baseTime.AddDays(day)), CancellationToken.None);
        }

        IReadOnlyList<DiagnosticRunSummary> recent = await service.GetRecentAsync(10, CancellationToken.None);

        Assert.Equal(3, recent.Count);
        Assert.Equal(baseTime.AddDays(5), recent[0].StartedAtUtc);
        Assert.Equal(baseTime.AddDays(4), recent[1].StartedAtUtc);
        Assert.Equal(baseTime.AddDays(3), recent[2].StartedAtUtc);
    }

    [Fact]
    public async Task CorruptJsonFile_DoesNotCrashAndIsIgnored()
    {
        Directory.CreateDirectory(_baseDirectory);
        string corruptFile = Path.Join(_baseDirectory, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(corruptFile, "{ this is not valid json");

        DiagnosticHistoryService service = CreateService();
        DiagnosticRun run = MakeRun();
        await service.SaveAsync(run, CancellationToken.None);

        IReadOnlyList<DiagnosticRunSummary> recent = await service.GetRecentAsync(10, CancellationToken.None);
        Assert.Single(recent);

        Guid corruptId = Guid.Parse(Path.GetFileNameWithoutExtension(corruptFile));
        Assert.Null(await service.GetByIdAsync(corruptId, CancellationToken.None));
        Assert.NotNull(await service.GetByIdAsync(run.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Save_IsAtomic_NoTempFileLeftBehind()
    {
        DiagnosticHistoryService service = CreateService();
        DiagnosticRun run = MakeRun();
        await service.SaveAsync(run, CancellationToken.None);

        Assert.Empty(Directory.EnumerateFiles(_baseDirectory, "*.tmp"));
        Assert.Single(Directory.EnumerateFiles(_baseDirectory, "*.json"));
    }
}
