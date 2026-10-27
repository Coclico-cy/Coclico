namespace Coclico.Modules.Diagnostics.Models;

public sealed record DiagnosticRequest
{
    public required DiagnosticMode Mode { get; init; }

    public IReadOnlySet<DiagnosticCategory> Categories { get; init; } =
        new HashSet<DiagnosticCategory>();

    public TimeSpan PerCheckTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public int MaxParallelChecks { get; init; } = 3;

    public static DiagnosticRequest Quick() => new() { Mode = DiagnosticMode.Quick };

    public static DiagnosticRequest Full() => new() { Mode = DiagnosticMode.Full };

    public static DiagnosticRequest Targeted(IEnumerable<DiagnosticCategory> categories)
        => new() { Mode = DiagnosticMode.Targeted, Categories = categories.ToHashSet() };
}

public sealed record DiagnosticProgress(
    string CurrentCheckId,
    DiagnosticCategory CurrentCategory,
    int CompletedChecks,
    int TotalChecks);

public sealed record DiagnosticEnvironment(
    bool IsElevated,
    bool HasBattery,
    bool HardwareSensorsEnabled,
    string OsVersion);

public sealed class DiagnosticContext
{
    public required DiagnosticEnvironment Environment { get; init; }

    public required DiagnosticRequest Request { get; init; }

    public required CancellationToken CancellationToken { get; init; }
}
