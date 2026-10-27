namespace Coclico.Modules.Diagnostics.Models;

public sealed class DiagnosticRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    public DiagnosticMode Mode { get; set; } = DiagnosticMode.Quick;

    public DiagnosticRunStatus Status { get; set; } = DiagnosticRunStatus.Running;

    public string ApplicationVersion { get; set; } = string.Empty;

    public string RulesVersion { get; set; } = string.Empty;

    public DiagnosticSystemSnapshot? SystemSnapshot { get; set; }

    public List<DiagnosticFinding> Findings { get; set; } = [];

    public List<DiagnosticCheckResult> Checks { get; set; } = [];

    public DiagnosticSummary? Summary { get; set; }

    public int SchemaVersion { get; set; } = 1;
}

public sealed record DiagnosticSystemSnapshot(
    string OsVersion,
    string OsArchitecture,
    int ProcessorCount,
    long TotalPhysicalMemoryBytes,
    bool IsElevated);

public sealed record DiagnosticSummary(
    int ChecksTotal,
    int ChecksCompleted,
    int ChecksSkipped,
    int ChecksFailed,
    int FindingsCritical,
    int FindingsError,
    int FindingsWarning,
    int FindingsInfo,
    int FindingsSuccess);

public sealed record DiagnosticRunSummary(
    Guid Id,
    DateTime StartedAtUtc,
    DiagnosticMode Mode,
    DiagnosticRunStatus Status,
    int FindingsWarning,
    int FindingsError,
    int FindingsCritical);
