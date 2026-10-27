using Coclico.Models.Network;

namespace Coclico.Services.Network;

public interface INetworkOptimizerService
{
    Task<IReadOnlyList<NetworkAdapterInfo>> GetNetworkAdaptersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<NetworkTuningParameter>> GetTuningStatusAsync(string? targetAdapterId = null, CancellationToken ct = default);
    Task<bool> CaptureBackupAsync(string? targetAdapterId = null, CancellationToken ct = default);
    Task<NetworkTuningReport> ApplyTuningAsync(NetworkTuningMode mode, string? targetAdapterId = null, Action<string>? onLog = null, Action<double>? onProgress = null, CancellationToken ct = default);
    Task<bool> RestoreInitialValuesAsync(string? targetAdapterId = null, Action<string>? onLog = null, CancellationToken ct = default);
    Task<bool> RestoreWindowsDefaultsAsync(string? targetAdapterId = null, Action<string>? onLog = null, CancellationToken ct = default);
    Task<NetworkTestReport> RunCompleteTestAsync(int runs = 4, Action<string, int>? onRunProgress = null, Action<double>? onProgress = null, CancellationToken ct = default);
    Task<(double PingMs, double JitterMs, double PacketLoss)> MeasureLatencyAsync(string targetHost = "1.1.1.1", int count = 10, CancellationToken ct = default);
}

public enum NetworkTuningMode
{
    AutoTested,
    Full
}

public interface INetworkBenchmarkService
{
    Task<(double MedianMs, double MeanMs, double JitterMs, double P95Ms, double PacketLossPercent)> RunStatisticalPingAsync(
        string host,
        int sampleCount = 10,
        int timeoutMs = 800,
        CancellationToken ct = default);

    Task<NetworkTestReport> RunSpeedTestAsync(
        int downloadRuns = 4,
        int uploadRuns = 2,
        Action<string, int>? onRunProgress = null,
        CancellationToken ct = default);
}
