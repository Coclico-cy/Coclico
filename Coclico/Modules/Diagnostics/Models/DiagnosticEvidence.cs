namespace Coclico.Modules.Diagnostics.Models;

public sealed record DiagnosticEvidence
{
    public required string MetricId { get; init; }

    public required string DisplayNameKey { get; init; }

    public string? Value { get; init; }

    public string Unit { get; init; } = string.Empty;

    public required string Source { get; init; }

    public required DateTime ObservedAtUtc { get; init; }

    public DiagnosticValueState State { get; init; } = DiagnosticValueState.Measured;

    public string? ExpectedRange { get; init; }

    public string? RawDetails { get; init; }

    public string? Entity { get; init; }

    public static DiagnosticEvidence Measured(
        string metricId,
        string displayNameKey,
        object? value,
        string unit,
        string source,
        string? entity = null,
        string? expectedRange = null,
        DateTime? observedAtUtc = null)
        => new()
        {
            MetricId = metricId,
            DisplayNameKey = displayNameKey,
            Value = value?.ToString() ?? "0",
            Unit = unit,
            Source = source,
            ObservedAtUtc = observedAtUtc ?? DateTime.UtcNow,
            State = DiagnosticValueState.Measured,
            Entity = entity,
            ExpectedRange = expectedRange
        };

    public static DiagnosticEvidence Unavailable(
        string metricId,
        string displayNameKey,
        DiagnosticValueState state,
        string source,
        string? entity = null,
        string? rawDetails = null)
        => new()
        {
            MetricId = metricId,
            DisplayNameKey = displayNameKey,
            Value = null,
            Source = source,
            ObservedAtUtc = DateTime.UtcNow,
            State = state,
            Entity = entity,
            RawDetails = rawDetails
        };
}
