#nullable enable
using System.Text.Json.Serialization;

namespace Coclico.Services;

public sealed record OptimizationAction(
    [property: JsonPropertyName("ProcessId")] int? ProcessId,
    [property: JsonPropertyName("ProcessName")] string ProcessName,
    [property: JsonPropertyName("TargetPriority")] string TargetPriority,
    [property: JsonPropertyName("Reason")] string Reason,
    [property: JsonPropertyName("NoAction")] bool NoAction = false,
    [property: JsonPropertyName("RcaSummary")] string? RcaSummary = null);
