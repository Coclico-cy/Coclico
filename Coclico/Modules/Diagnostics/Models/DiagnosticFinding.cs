namespace Coclico.Modules.Diagnostics.Models;

public sealed record DiagnosticFinding
{
    public required string Id { get; init; }

    public string? Entity { get; init; }

    public required DiagnosticCategory Category { get; init; }

    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Info;

    public DiagnosticConfidence Confidence { get; init; } = DiagnosticConfidence.Medium;

    public required string TitleKey { get; init; }

    public string? SummaryKey { get; init; }

    public string? ExplanationKey { get; init; }

    public required DateTime ObservedAtUtc { get; init; }

    public IReadOnlyList<DiagnosticEvidence> Evidence { get; init; } = [];

    public IReadOnlyList<string> PossibleCauseKeys { get; init; } = [];

    public IReadOnlyList<string> RecommendedNextStepKeys { get; init; } = [];

    public IReadOnlyList<string> RelatedCheckIds { get; init; } = [];

    public IReadOnlyList<string> ActionIds { get; init; } = [];

    public bool IsActionable { get; init; }

    public bool IsFactBacked { get; init; } = true;

    public string DedupeKey => Entity is null ? Id : $"{Id}|{Entity}";
}
