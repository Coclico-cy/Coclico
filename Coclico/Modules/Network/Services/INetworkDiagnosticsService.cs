using Coclico.Models.Network;

namespace Coclico.Services.Network;

public interface INetworkDiagnosticsService
{
    IReadOnlyList<string> DiagnoseBottlenecks(NetworkAdapterHardwareDetails adapter, NetworkMetricReport metrics);
    string GenerateStorytellingReport(
        NetworkAdapterHardwareDetails adapter,
        OptimizationProfile profile,
        NetworkMetricReport baseline,
        NetworkMetricReport finalMetrics,
        IReadOnlyList<ExperimentRecord> experiments);
}
