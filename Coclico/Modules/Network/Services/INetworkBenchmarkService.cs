using Coclico.Models.Network;

namespace Coclico.Services.Network;

public interface INetworkBenchmarkService
{
    Task<NetworkMetricReport> RunComprehensiveBenchmarkAsync(
        string? gatewayAddress = null,
        int pingSamples = 10,
        bool includeBufferbloat = true,
        bool includeDns = true,
        Action<string, double>? progress = null,
        CancellationToken ct = default);

    Task<NetworkMetricReport> RunFourPrecisionTestsAsync(
        string? gatewayAddress = null,
        int samplesPerTest = 10,
        Action<int, string, int, int, double>? onSampleProgress = null,
        CancellationToken ct = default);

    Task<(double MedianMs, double MeanMs, double JitterMs, double P95Ms, double PacketLossPercent)> RunStatisticalPingAsync(
        string host,
        int sampleCount = 10,
        int timeoutMs = 800,
        CancellationToken ct = default);

    Task<double> MeasureRealThroughputMbpsAsync(
        int samplesCount = 10,
        Action<int, int, double>? onSampleProgress = null,
        CancellationToken ct = default);

    Task<(double BufferbloatLatencyMs, string Grade)> MeasureBufferbloatAsync(
        string host = "1.1.1.1",
        CancellationToken ct = default);

    Task<(double ResolutionMs, double SuccessRatePercent)> BenchmarkRealDnsResolutionAsync(
        IReadOnlyList<string>? testDomains = null,
        CancellationToken ct = default);

    Task<MtuDetectionResult> DetectOptimalMtuAsync(
        string targetHost = "1.1.1.1",
        CancellationToken ct = default);
}