using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class DiagnosticSummaryBuilderTests
{
    private static DiagnosticFinding MakeFinding(DiagnosticSeverity severity) => new()
    {
        Id = "R" + Guid.NewGuid().ToString("N"),
        Category = DiagnosticCategory.Storage,
        Severity = severity,
        TitleKey = "Diag_Finding_LowSpace_Title",
        ObservedAtUtc = DateTime.UtcNow
    };

    private static DiagnosticCheckResult MakeCheck(DiagnosticCheckStatus status) => new()
    {
        CheckId = "check." + status,
        Category = DiagnosticCategory.Cpu,
        Status = status
    };

    [Fact]
    public void Build_CountsFindingsBySeverity()
    {
        DiagnosticRun run = new()
        {
            Findings =
            [
                MakeFinding(DiagnosticSeverity.Critical),
                MakeFinding(DiagnosticSeverity.Error),
                MakeFinding(DiagnosticSeverity.Error),
                MakeFinding(DiagnosticSeverity.Warning),
                MakeFinding(DiagnosticSeverity.Info),
                MakeFinding(DiagnosticSeverity.Success)
            ]
        };

        DiagnosticSummary summary = DiagnosticSummaryBuilder.Build(run);

        Assert.Equal(1, summary.FindingsCritical);
        Assert.Equal(2, summary.FindingsError);
        Assert.Equal(1, summary.FindingsWarning);
        Assert.Equal(1, summary.FindingsInfo);
        Assert.Equal(1, summary.FindingsSuccess);
    }

    [Fact]
    public void Build_CountsChecksByOutcome()
    {
        DiagnosticRun run = new()
        {
            Checks =
            [
                MakeCheck(DiagnosticCheckStatus.Passed),
                MakeCheck(DiagnosticCheckStatus.Findings),
                MakeCheck(DiagnosticCheckStatus.Skipped),
                MakeCheck(DiagnosticCheckStatus.Unsupported),
                MakeCheck(DiagnosticCheckStatus.Cancelled),
                MakeCheck(DiagnosticCheckStatus.Failed),
                MakeCheck(DiagnosticCheckStatus.TimedOut)
            ]
        };

        DiagnosticSummary summary = DiagnosticSummaryBuilder.Build(run);

        Assert.Equal(7, summary.ChecksTotal);
        Assert.Equal(2, summary.ChecksCompleted);
        Assert.Equal(3, summary.ChecksSkipped);
        Assert.Equal(2, summary.ChecksFailed);
    }

    [Fact]
    public void Build_EmptyRun_GivesZeroCounters()
    {
        DiagnosticSummary summary = DiagnosticSummaryBuilder.Build(new DiagnosticRun());

        Assert.Equal(0, summary.ChecksTotal);
        Assert.Equal(0, summary.FindingsCritical);
        Assert.Equal(0, summary.FindingsError);
        Assert.Equal(0, summary.FindingsWarning);
    }

    [Fact]
    public void Build_ContainsNoGlobalScore()
    {
        DiagnosticSummaryBuilder.Build(new DiagnosticRun());

        string[] propertyNames = typeof(DiagnosticSummary)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();

        Assert.Equal(
            ["ChecksCompleted", "ChecksFailed", "ChecksSkipped", "ChecksTotal",
             "FindingsCritical", "FindingsError", "FindingsInfo", "FindingsSuccess", "FindingsWarning"],
            propertyNames.OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(propertyNames, name => name.Contains("Score", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_NullRun_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DiagnosticSummaryBuilder.Build(null!));
    }
}
