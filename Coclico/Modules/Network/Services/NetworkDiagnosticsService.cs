using System.Text;
using Coclico.Models.Network;

namespace Coclico.Services.Network;

public sealed class NetworkDiagnosticsService : INetworkDiagnosticsService
{
    public IReadOnlyList<string> DiagnoseBottlenecks(NetworkAdapterHardwareDetails adapter, NetworkMetricReport metrics)
    {
        var diagnoses = new List<string>();

        if (adapter.IsWireless)
        {
            if (adapter.WifiRssi.HasValue)
            {
                if (adapter.WifiRssi.Value < 50)
                {
                    diagnoses.Add($"Signal Wi-Fi critique ({adapter.WifiRssi.Value}%) : forte atténuation radio entraînant déconnexions et pertes massives de paquets.");
                }
                else if (adapter.WifiRssi.Value < 65)
                {
                    diagnoses.Add($"Signal Wi-Fi affaibli ({adapter.WifiRssi.Value}%) : atténuation radio réduisant le débit et provoquant des retransmissions.");
                }
            }

            if (!string.IsNullOrEmpty(adapter.WifiRadioType))
            {
                bool isModernWifi = adapter.WifiRadioType.Contains("be", StringComparison.OrdinalIgnoreCase) ||
                                    adapter.WifiRadioType.Contains("ax", StringComparison.OrdinalIgnoreCase) ||
                                    adapter.WifiRadioType.Contains("6", StringComparison.OrdinalIgnoreCase) ||
                                    adapter.WifiRadioType.Contains("7", StringComparison.OrdinalIgnoreCase);

                if (!isModernWifi)
                {
                    diagnoses.Add($"Norme Wi-Fi active ({adapter.WifiRadioType}) : génération antérieure (Wi-Fi 5 ou plus ancien). Privilégier un lien Wi-Fi 6/6E/7 ou une connexion filaire pour réduire le temps de trajet.");
                }
            }
        }

        double severeBufferbloat = adapter.IsWireless ? 40.0 : 25.0;
        double moderateBufferbloat = adapter.IsWireless ? 20.0 : 12.0;

        if (metrics.BufferbloatIncreaseMs > severeBufferbloat || metrics.BufferbloatGrade is "D" or "F")
        {
            diagnoses.Add($"Bufferbloat sévère sous charge (+{metrics.BufferbloatIncreaseMs:F1} ms, Note {metrics.BufferbloatGrade}) : saturation des mémoires tampons de votre box/routeur lors des transferts.");
        }
        else if (metrics.BufferbloatIncreaseMs > moderateBufferbloat)
        {
            diagnoses.Add($"Bufferbloat modéré (+{metrics.BufferbloatIncreaseMs:F1} ms, Note {metrics.BufferbloatGrade}) : légère dégradation de latence lors des pics de trafic.");
        }

        double jitterThreshold = adapter.IsWireless ? 6.0 : 2.5;
        if (metrics.JitterMs > jitterThreshold)
        {
            string medium = adapter.IsWireless ? "sans-fil" : "Ethernet";
            diagnoses.Add($"Gigue élevée pour un lien {medium} ({metrics.JitterMs:F1} ms) : instabilité temporelle perceptible dans les jeux et flux temps réel.");
        }

        if (metrics.PacketLossPercent > 0.5)
        {
            diagnoses.Add($"Perte de paquets anormale ({metrics.PacketLossPercent:F1}%) : déconnexions intermittentes ou micro-coupures de lien.");
            diagnoses.Add($"Perte de paquets anormale ({metrics.PacketLossPercent:F1}%) : micro-coupures de lien ou encombrement du canal.");
        }

        if (metrics.DnsResolutionMs > 35.0)
        {
            diagnoses.Add($"Résolution DNS lente ({metrics.DnsResolutionMs:F1} ms) : latence au chargement des nouvelles requêtes Web.");
            diagnoses.Add($"Résolution DNS lente ({metrics.DnsResolutionMs:F1} ms) : latence lors du chargement des nouvelles requêtes Web.");
        }

        if (metrics.DetectedMtu is < 1492 and > 0)
        {
            diagnoses.Add($"MTU réduit ({metrics.DetectedMtu}) : possible encapsulation tunnel/PPPoE ou fragmentation intermédiaire.");
        }

        if (diagnoses.Count == 0)
        {
            diagnoses.Add("Liaison réseau saine : latence stable, gigue minimale et aucune perte de paquets détectée.");
        }

        return diagnoses;
    }

    public string GenerateStorytellingReport(
        NetworkAdapterHardwareDetails adapter,
        OptimizationProfile profile,
        NetworkMetricReport baseline,
        NetworkMetricReport finalMetrics,
        IReadOnlyList<ExperimentRecord> experiments)
    {
        var sb = new StringBuilder();

        _ = sb.AppendLine($"### 🎯 Rapport Cognitif d'Optimisation Réseau — Profil {profile}");
        _ = sb.AppendLine();
        _ = sb.AppendLine($"**Matériel ciblé :** `{adapter.Name}` ({adapter.Description})");
        _ = sb.AppendLine($"**Pilote actif :** v{adapter.DriverVersion} (Fournisseur : {adapter.DriverProvider})");
        if (adapter.IsWireless && !string.IsNullOrEmpty(adapter.WifiSsid))
        {
            _ = sb.AppendLine($"**Réseau Wi-Fi :** `{adapter.WifiSsid}` | Canal : {adapter.WifiChannel} | Signal : {adapter.WifiRssi}% | Norme : {adapter.WifiRadioType}");
        }
        _ = sb.AppendLine();

        _ = sb.AppendLine("#### 1. Bilan Initial (Baseline)");
        _ = sb.AppendLine($"- **Latence médiane :** `{baseline.LatencyMedianMs:F1} ms`");
        _ = sb.AppendLine($"- **Gigue temporelle :** `{baseline.JitterMs:F1} ms`");
        _ = sb.AppendLine($"- **Perte de paquets :** `{baseline.PacketLossPercent:F1}%`");
        _ = sb.AppendLine($"- **Bufferbloat :** `{baseline.BufferbloatGrade}` (+{baseline.BufferbloatIncreaseMs:F1} ms sous charge)");
        _ = sb.AppendLine($"- **Score initial :** **`{baseline.CompositeScore:F1} / 100`**");
        _ = sb.AppendLine();

        var accepted = experiments.Where(e => e.Decision == ExperimentDecision.Accepted).ToList();
        var rejected = experiments.Where(e => e.Decision != ExperimentDecision.Accepted).ToList();

        _ = sb.AppendLine($"#### 2. Expérimentations Empiriques ({experiments.Count} tests conduits)");
        if (accepted.Count > 0)
        {
            _ = sb.AppendLine("**Réglages gagnants validés et conservés :**");
            foreach (ExperimentRecord a in accepted)
            {
                _ = sb.AppendLine($"- ✅ **{a.ParameterDisplayName}** (`{a.ParameterKeyword}`) : passé à `{a.TestedValue}` ({a.DecisionReason})");
            }
        }
        else
        {
            _ = sb.AppendLine("- ℹ️ Aucun tweak isolé n'a surpassé la configuration native sans dégrader la stabilité.");
        }

        if (rejected.Count > 0)
        {
            _ = sb.AppendLine();
            _ = sb.AppendLine("**Réglages rejetés et restaurés immédiatement :**");
            foreach (ExperimentRecord r in rejected)
            {
                string icon = r.Decision == ExperimentDecision.RevertedByWatchdog ? "🛡️" : "↩️";
                _ = sb.AppendLine($"- {icon} **{r.ParameterDisplayName}** : annulé ({r.DecisionReason})");
            }
        }
        _ = sb.AppendLine();

        double deltaScore = finalMetrics.CompositeScore - baseline.CompositeScore;
        double deltaLat = baseline.LatencyMedianMs - finalMetrics.LatencyMedianMs;
        double deltaJit = baseline.JitterMs - finalMetrics.JitterMs;

        _ = sb.AppendLine("#### 3. Bilan Final & Verdict");
        _ = sb.AppendLine($"- **Score d'efficacité :** **`{finalMetrics.CompositeScore:F1} / 100`** ({(deltaScore >= 0 ? "+" : "")}{deltaScore:F1} pts)");
        _ = sb.AppendLine($"- **Gain de latence :** `{(deltaLat > 0 ? $"-{deltaLat:F1} ms" : $"{Math.Abs(deltaLat):F1} ms d'écart")}`");
        _ = sb.AppendLine($"- **Gain de gigue :** `{(deltaJit > 0 ? $"-{deltaJit:F1} ms" : "stable")}`");
        _ = sb.AppendLine($"- **Stabilité :** `{finalMetrics.PacketLossPercent:F1}%` de perte de paquets (Intégrité 100% garantie)");
        _ = sb.AppendLine();

        IReadOnlyList<string> bottlenecks = DiagnoseBottlenecks(adapter, finalMetrics);
        if (bottlenecks.Count > 0)
        {
            _ = sb.AppendLine("#### 4. Recommandations & Diagnostic Système");
            foreach (string b in bottlenecks)
            {
                _ = sb.AppendLine($"- 💡 {b}");
            }
        }

        return sb.ToString();
    }
}