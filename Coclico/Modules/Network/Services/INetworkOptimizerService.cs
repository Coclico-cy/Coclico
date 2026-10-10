using Coclico.Models.Network;

namespace Coclico.Services.Network;

public interface INetworkOptimizerService
{
    INetworkExperimentEngine Engine { get; }
    INetworkDiscoveryService DiscoveryService { get; }
    INetworkSnapshotService SnapshotService { get; }

    Task<IReadOnlyList<NetworkAdapterInfo>> GetNetworkAdaptersAsync(CancellationToken ct = default);
    Task<TcpGlobalSettings> GetTcpSettingsAsync(CancellationToken ct = default);
    Task<NicHardwareSettings> GetNicHardwareSettingsAsync(string adapterIdOrGuid, CancellationToken ct = default);
    Task<bool> ApplyCustomTcpSettingsAsync(TcpGlobalSettings settings, string? adapterId = null, CancellationToken ct = default);
    Task<bool> ApplyCustomNicSettingsAsync(string adapterIdOrGuid, NicHardwareSettings settings, CancellationToken ct = default);
    Task<NetworkBenchmarkReport> ApplyPresetAsync(NetworkOptimizationPreset preset, string? targetAdapterId = null, Action<string, double>? progress = null, CancellationToken ct = default);
    Task<IReadOnlyList<DnsBenchmarkResult>> RunDnsBenchmarkAsync(Action<string, double>? progress = null, CancellationToken ct = default);
    Task<bool> ApplyDnsAsync(string adapterIdOrName, string primaryDns, string secondaryDns, CancellationToken ct = default);
    Task<MtuDetectionResult> DetectOptimalMtuAsync(string targetHost = "1.1.1.1", Action<string, double>? progress = null, CancellationToken ct = default);
    Task<bool> ApplyMtuAsync(string adapterName, int mtu, CancellationToken ct = default);
    Task<NetworkBenchmarkReport> RunAutoTuningOptimizationAsync(string? targetAdapterId = null, Action<string, double>? progress = null, CancellationToken ct = default);
    Task<NetworkBenchmarkReport> RunComprehensiveCalibrationAsync(string? targetAdapterId, bool deepMode, Action<CalibrationStepProgress>? stepProgress = null, Action<string, double>? progress = null, CancellationToken ct = default);
    Task<string> RunAdaptiveEngineSessionAsync(string? targetAdapterId = null, OptimizationProfile profile = OptimizationProfile.Gaming, EngineExecutionMode mode = EngineExecutionMode.Auto, int samplesPerTest = 10, Action<EngineProgressInfo>? onProgress = null, Action<CognitiveLogEntry>? onLog = null, CancellationToken ct = default);
    Task<string> RunGlobalUnifiedOptimizationAsync(string? targetAdapterId = null, OptimizationProfile profile = OptimizationProfile.Gaming, EngineExecutionMode mode = EngineExecutionMode.Auto, Action<EngineProgressInfo>? onProgress = null, Action<CognitiveLogEntry>? onLog = null, CancellationToken ct = default);
    Task<bool> RestoreWindowsDefaultsAsync(string? targetAdapterId = null, Action<string, double>? progress = null, CancellationToken ct = default);
    Task<(double PingMs, double JitterMs, double PacketLoss)> MeasureLatencyAsync(string targetHost = "1.1.1.1", int count = 5, CancellationToken ct = default);
}
