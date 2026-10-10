using Coclico.Models.Network;

namespace Coclico.Services.Network;

public class NetworkScoringService : INetworkScoringService
{
    public double ComputeCompositeScore(NetworkMetricReport report, OptimizationProfile profile)
    {
        double latScore = Math.Clamp(100.0 - (report.LatencyMedianMs * 0.7), 0.0, 100.0);

        double jitScore = Math.Clamp(100.0 - (report.JitterMs * 5.0), 0.0, 100.0);

        double lossScore = Math.Clamp(100.0 - (report.PacketLossPercent * 25.0), 0.0, 100.0);

        double bloatScore = report.BufferbloatGrade switch
        {
            "A+" => 100.0,
            "A" => 90.0,
            "B" => 75.0,
            "C" => 55.0,
            "D" => 35.0,
            _ => 10.0
        };

        double dnsScore = Math.Clamp(100.0 - (report.DnsResolutionMs * 0.8), 0.0, 100.0);
        if (report.DnsSuccessRatePercent < 100)
        {
            dnsScore *= report.DnsSuccessRatePercent / 100.0;
        }

        double finalScore = profile switch
        {
            OptimizationProfile.Gaming =>
                (latScore * 0.35) + (jitScore * 0.30) + (lossScore * 0.25) + (bloatScore * 0.10),

            OptimizationProfile.LowLatency =>
                (latScore * 0.40) + (jitScore * 0.35) + (lossScore * 0.25),

            OptimizationProfile.MaxThroughput =>
                (bloatScore * 0.35) + (lossScore * 0.35) + (jitScore * 0.15) + (latScore * 0.15),

            OptimizationProfile.Streaming =>
                (lossScore * 0.35) + (jitScore * 0.30) + (bloatScore * 0.20) + (latScore * 0.15),

            OptimizationProfile.WifiSpecific =>
                (jitScore * 0.35) + (lossScore * 0.30) + (latScore * 0.25) + (bloatScore * 0.10),

            _ =>
                (latScore * 0.30) + (jitScore * 0.25) + (lossScore * 0.25) + (bloatScore * 0.10) + (dnsScore * 0.10)
        };

        report.CompositeScore = Math.Round(Math.Clamp(finalScore, 0.0, 100.0), 1);
        return report.CompositeScore;
    }

    public (bool IsImprovement, ExperimentDecision Decision, string Reason) CompareMetrics(
        NetworkMetricReport baseline,
        NetworkMetricReport tested,
        OptimizationProfile profile)
    {
        if (tested.PacketLossPercent > baseline.PacketLossPercent + 0.5)
        {
            return (false, ExperimentDecision.RejectedInstability,
                $"Perte de paquets en hausse ({tested.PacketLossPercent:F1}% vs {baseline.PacketLossPercent:F1}%). Rejet pour préserver la stabilité.");
        }

        if (tested.JitterMs > baseline.JitterMs + 2.0 && tested.JitterMs > baseline.JitterMs * 1.35)
        {
            return (false, ExperimentDecision.RejectedInstability,
                $"Gigue excessive (+{tested.JitterMs - baseline.JitterMs:F1} ms). Dégradation de la régularité du flux.");
        }

        if (tested.LatencyMedianMs > baseline.LatencyMedianMs + 3.0 && tested.LatencyMedianMs > baseline.LatencyMedianMs * 1.15)
        {
            return (false, ExperimentDecision.RejectedRegression,
                $"Latence médiane dégradée (+{tested.LatencyMedianMs - baseline.LatencyMedianMs:F1} ms).");
        }

        double baselineScore = ComputeCompositeScore(baseline, profile);
        double testedScore = ComputeCompositeScore(tested, profile);

        if (testedScore >= baselineScore + 0.4)
        {
            double deltaLat = baseline.LatencyMedianMs - tested.LatencyMedianMs;
            double deltaJit = baseline.JitterMs - tested.JitterMs;
            string details = "";
            if (deltaLat > 0.2)
            {
                details += $"-{deltaLat:F1}ms ping ";
            }

            if (deltaJit > 0.2)
            {
                details += $"-{deltaJit:F1}ms gigue ";
            }

            if (string.IsNullOrEmpty(details))
            {
                details = $"Score +{testedScore - baselineScore:F1} pts";
            }

            return (true, ExperimentDecision.Accepted, $"Amélioration mesurée ({details.Trim()}). Configuration conservée.");
        }

        return (false, ExperimentDecision.RejectedRegression,
            $"Gain insuffisant ou inexistant (Score {testedScore:F1} vs {baselineScore:F1}). Rejet du paramètre.");
    }
}
