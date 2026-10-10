using Coclico.Models.Network;

namespace Coclico.Services.Network;

public interface INetworkScoringService
{
    double ComputeCompositeScore(NetworkMetricReport report, OptimizationProfile profile);
    (bool IsImprovement, ExperimentDecision Decision, string Reason) CompareMetrics(
        NetworkMetricReport baseline,
        NetworkMetricReport tested,
        OptimizationProfile profile);
}
