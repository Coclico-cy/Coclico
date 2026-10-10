using Coclico.Models.Network;

namespace Coclico.Services.Network;

public interface INetworkExperimentEngine
{
    EngineState CurrentState { get; }
    EngineProgressInfo CurrentProgress { get; }
    event Action<EngineProgressInfo>? OnProgressUpdated;
    event Action<CognitiveLogEntry>? OnLogEntryAdded;

    Task<string> RunAdaptiveOptimizationSessionAsync(
        string? targetAdapterId = null,
        OptimizationProfile profile = OptimizationProfile.Gaming,
        EngineExecutionMode mode = EngineExecutionMode.Auto,
        int samplesPerTest = 10,
        CancellationToken ct = default);

    Task CancelSessionAsync();
    Task RequestEmergencyRollbackAsync();
}

public enum EngineState
{
    Idle,
    Running,
    Paused,
    RollingBack,
    Completed,
    Cancelled,
    Failed
}
