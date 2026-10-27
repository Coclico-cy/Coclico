namespace Coclico.Modules.Diagnostics.Models;

public sealed record DiagnosticCheckResult
{
    public required string CheckId { get; init; }

    public required DiagnosticCategory Category { get; init; }

    public DiagnosticCheckStatus Status { get; init; } = DiagnosticCheckStatus.Passed;

    public bool IsQuick { get; init; }

    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;

    public TimeSpan Duration { get; init; }

    public IReadOnlyList<DiagnosticFinding> Findings { get; init; } = [];

    public string? Reason { get; init; }

    public static DiagnosticCheckResult Finished(
        string checkId,
        DiagnosticCategory category,
        bool isQuick,
        DiagnosticCheckStatus status,
        IReadOnlyList<DiagnosticFinding> findings,
        TimeSpan duration,
        string? reason = null)
        => new()
        {
            CheckId = checkId,
            Category = category,
            IsQuick = isQuick,
            Status = status,
            Findings = findings,
            Duration = duration,
            Reason = reason
        };
}
