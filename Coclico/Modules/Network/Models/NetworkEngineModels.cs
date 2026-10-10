namespace Coclico.Models.Network;

public enum ParameterSafetyLevel
{
    ReadOnly = 0,
    Safe = 1,
    LowRisk = 2,
    MediumRisk = 3,
    HighRisk = 4,
    Dangerous = 5,
    Unknown = 6
}

public enum ParameterCategory
{
    HardwareOffload,
    InterruptHandling,
    EnergyManagement,
    BuffersAndQueues,
    FlowControl,
    WirelessRadio,
    TcpStack,
    DnsAndResolution,
    MtuAndFraming,
    SystemThrottling,
    Unknown
}

public enum EngineExecutionMode
{
    ReadOnly,
    Simulation,
    Safe,
    Auto,
    Expert
}

public enum OptimizationProfile
{
    Gaming,
    LowLatency,
    MaxThroughput,
    Balanced,
    Streaming,
    Work,
    WifiSpecific,
    EthernetSpecific
}

public enum DynamicParameterType
{
    Enumeration,
    IntegerRange,
    Numeric,
    Text,
    Boolean
}

public sealed class DynamicParameterOption
{
    public required string DisplayName { get; init; }
    public required string RegistryValue { get; init; }
    public string Description { get; init; } = string.Empty;
}

public sealed class DynamicNetworkParameter
{
    public required string RegistryKeyword { get; init; }
    public required string DisplayName { get; init; }
    public string CurrentValue { get; set; } = string.Empty;
    public string CurrentDisplayValue { get; set; } = string.Empty;
    public ParameterCategory Category { get; set; } = ParameterCategory.Unknown;
    public ParameterSafetyLevel SafetyLevel { get; set; } = ParameterSafetyLevel.Unknown;
    public DynamicParameterType ValueType { get; set; } = DynamicParameterType.Text;
    public List<DynamicParameterOption> ValidOptions { get; init; } = [];
    public long? MinValue { get; set; }
    public long? MaxValue { get; set; }
    public long? Step { get; set; }
    public string DefaultValue { get; set; } = string.Empty;
    public string ImpactDescription { get; set; } = string.Empty;
    public string DetailedExplanation { get; set; } = string.Empty;
    public string Scope { get; set; } = "NDIS Pilote";
    public bool IsModifiable => SafetyLevel is not ParameterSafetyLevel.ReadOnly and
                                not ParameterSafetyLevel.Dangerous and
                                not ParameterSafetyLevel.Unknown;
}

public sealed class NetworkAdapterHardwareDetails
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public NetworkAdapterType Type { get; init; }
    public bool IsPhysical { get; init; }
    public string PnpInstanceId { get; init; } = string.Empty;
    public string MatchingDeviceId { get; init; } = string.Empty;
    public string DriverVersion { get; init; } = string.Empty;
    public string DriverDate { get; init; } = string.Empty;
    public string DriverProvider { get; init; } = string.Empty;
    public long LinkSpeedBitsPerSecond { get; set; }
    public string MacAddress { get; init; } = string.Empty;
    public string IPv4Address { get; set; } = string.Empty;
    public string GatewayAddress { get; set; } = string.Empty;
    public int Mtu { get; set; } = 1500;
    public List<string> DnsServers { get; init; } = [];

    public bool IsWireless => Type == NetworkAdapterType.Wireless80211;
    public string? WifiSsid { get; set; }
    public string? WifiBssid { get; set; }
    public int? WifiRssi { get; set; }
    public int? WifiChannel { get; set; }
    public string? WifiRadioType { get; set; }

    public List<DynamicNetworkParameter> DiscoveredParameters { get; init; } = [];
}

public sealed class NetworkMetricReport
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public double LatencyMedianMs { get; set; }
    public double LatencyMeanMs { get; set; }
    public double LatencyP95Ms { get; set; }
    public double LatencyP99Ms { get; set; }
    public double LatencyMinMs { get; set; }
    public double LatencyMaxMs { get; set; }
    public double JitterMs { get; set; }
    public double PacketLossPercent { get; set; }
    public int SampleCount { get; set; }

    public double GatewayLatencyMs { get; set; }
    public double InternetLatencyMs { get; set; }

    public double BufferbloatLatencyUnderLoadMs { get; set; }
    public double BufferbloatIncreaseMs => Math.Max(0, BufferbloatLatencyUnderLoadMs - LatencyMedianMs);
    public string BufferbloatGrade { get; set; } = "A";

    public double DnsResolutionMs { get; set; }
    public double DnsSuccessRatePercent { get; set; } = 100.0;

    public double ThroughputMbps { get; set; }

    public int DetectedMtu { get; set; } = 1500;

    public double CompositeScore { get; set; }
}

public sealed class NetworkConfigurationSnapshot
{
    public string SnapshotId { get; init; } = Guid.NewGuid().ToString("N");
    public string Label { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public string AdapterId { get; init; } = string.Empty;
    public string AdapterName { get; init; } = string.Empty;
    public int Mtu { get; set; } = 1500;
    public List<string> DnsServers { get; set; } = [];
    public Dictionary<string, string> NicRegistrySettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public TcpGlobalSettings TcpSettings { get; set; } = new();
    public NetworkMetricReport? MetricsAtSnapshot { get; set; }
}

public enum ExperimentDecision
{
    Accepted,
    RejectedRegression,
    RejectedInstability,
    RevertedByWatchdog,
    DiscardedUnknown,
    SimulationOnly,
    KeptBaseline
}

public sealed class ExperimentRecord
{
    public int ExperimentIndex { get; init; }
    public string ParameterKeyword { get; init; } = string.Empty;
    public string ParameterDisplayName { get; init; } = string.Empty;
    public string PreviousValue { get; init; } = string.Empty;
    public string TestedValue { get; init; } = string.Empty;
    public ParameterSafetyLevel SafetyLevel { get; init; }
    public string ParameterScope { get; set; } = "NDIS Pilote";
    public string ParameterCategoryName { get; set; } = "Général";
    public string ParameterExplanation { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan Duration { get; set; }
    public NetworkMetricReport? MetricsBefore { get; set; }
    public NetworkMetricReport? MetricsAfter { get; set; }

    public double LatencyBeforeMs => MetricsBefore?.LatencyMedianMs ?? 0;
    public double LatencyAfterMs => MetricsAfter?.LatencyMedianMs ?? 0;
    public double JitterBeforeMs => MetricsBefore?.JitterMs ?? 0;
    public double JitterAfterMs => MetricsAfter?.JitterMs ?? 0;
    public double ThroughputBeforeMbps => MetricsBefore?.ThroughputMbps ?? 0;
    public double ThroughputAfterMbps => MetricsAfter?.ThroughputMbps ?? 0;
    public string BufferbloatBefore => MetricsBefore?.BufferbloatGrade ?? "A";
    public string BufferbloatAfter => MetricsAfter?.BufferbloatGrade ?? "A";
    public int SamplesTestedCount { get; set; } = 40;

    public double ScoreDelta => (MetricsAfter?.CompositeScore ?? 0) - (MetricsBefore?.CompositeScore ?? 0);
    public ExperimentDecision Decision { get; set; }
    public string DecisionReason { get; set; } = string.Empty;

    public string DecisionDisplay => Decision switch
    {
        ExperimentDecision.Accepted => "Validé & Conservé",
        ExperimentDecision.KeptBaseline => "Déjà Optimal (Maintenu)",
        ExperimentDecision.RejectedRegression => "Rejeté (Régression)",
        ExperimentDecision.RejectedInstability => "Rejeté (Instabilité)",
        ExperimentDecision.RevertedByWatchdog => "Annulé par Watchdog",
        ExperimentDecision.SimulationOnly => "Testé (Simulation)",
        ExperimentDecision.DiscardedUnknown => "Ignoré",
        _ => "En attente"
    };

    public string DecisionColorHex => Decision switch
    {
        ExperimentDecision.Accepted => "#10B981",
        ExperimentDecision.KeptBaseline => "#34D399",
        ExperimentDecision.RejectedRegression => "#EF4444",
        ExperimentDecision.RejectedInstability => "#F59E0B",
        ExperimentDecision.RevertedByWatchdog => "#DC2626",
        ExperimentDecision.SimulationOnly => "#3B82F6",
        _ => "#6B7280"
    };

    public string ScopeColorHex => ParameterScope switch
    {
        var s when s.Contains("NDIS", StringComparison.OrdinalIgnoreCase) => "#6366F1",
        var s when s.Contains("TCP", StringComparison.OrdinalIgnoreCase) => "#0EA5E9",
        var s when s.Contains("QoS", StringComparison.OrdinalIgnoreCase) => "#8B5CF6",
        var s when s.Contains("MTU", StringComparison.OrdinalIgnoreCase) => "#10B981",
        _ => "#64748B"
    };

    public string ImpactBadgeText => Decision switch
    {
        ExperimentDecision.Accepted when ScoreDelta > 0 => $"+{ScoreDelta:F1} pts",
        ExperimentDecision.Accepted when LatencyBeforeMs > LatencyAfterMs => $"-{LatencyBeforeMs - LatencyAfterMs:F1} ms",
        ExperimentDecision.Accepted => "Optimal",
        ExperimentDecision.KeptBaseline => "Optimal",
        ExperimentDecision.RejectedRegression => "Régression",
        ExperimentDecision.RejectedInstability => "Gigue / Pertes",
        ExperimentDecision.RevertedByWatchdog => "Déconnexion",
        ExperimentDecision.SimulationOnly => "Simulé",
        _ => "--"
    };
}

public sealed class HardwareKnowledgeProfile
{
    public string HardwareFingerprint { get; set; } = string.Empty;
    public string AdapterDescription { get; set; } = string.Empty;
    public string DriverVersion { get; set; } = string.Empty;
    public DateTime LastOptimizedUtc { get; set; }
    public Dictionary<string, string> ProvenBestSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> KnownHarmfulKeywords { get; set; } = [];
    public double BestScoreAchieved { get; set; }
    public List<ExperimentRecord> HistoricalExperiments { get; set; } = [];
}

public sealed class NetworkMemoryStore
{
    public int Version { get; set; } = 1;
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, HardwareKnowledgeProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum EnginePhase
{
    Idle,
    Discovery,
    SafetyAudit,
    InitialSnapshot,
    BaselineBenchmark,
    SingleParameterExperiments,
    InteractionExperiments,
    ValidationVerification,
    StorytellingSummary,
    Rollback,
    Completed,
    Failed
}

public sealed class CognitiveLogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public EnginePhase Phase { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Level { get; init; } = "INFO";
    public double? ProgressPercentage { get; init; }
}

public sealed class EngineProgressInfo
{
    public EnginePhase CurrentPhase { get; set; } = EnginePhase.Idle;
    public string StatusMessage { get; set; } = "Prêt";
    public double OverallProgress { get; set; }
    public int CurrentExperiment { get; set; }
    public int TotalExperiments { get; set; }
    public string CurrentTestedParameter { get; set; } = string.Empty;
    public string CurrentTestedParamExplanation { get; set; } = string.Empty;
    public string CurrentTestedParamCategory { get; set; } = string.Empty;
    public string CurrentTestedParamScope { get; set; } = string.Empty;
    public string CurrentTestedValue { get; set; } = string.Empty;
    public string PreviousTestedValue { get; set; } = string.Empty;

    public int CurrentTestIndex { get; set; } = 1;
    public string CurrentTestName { get; set; } = "Test 1/4 : Latence Haute-Précision";
    public int CurrentSampleIndex { get; set; } = 1;
    public int TotalSamplesPerTest { get; set; } = 10;

    public double LivePrecisionLatencyMs { get; set; }
    public double LivePrecisionJitterMs { get; set; }
    public double LivePrecisionThroughputMbps { get; set; }
    public double LivePrecisionBufferbloatMs { get; set; }

    public double BaselineScore { get; set; }
    public double BaselineLatencyMs { get; set; }
    public double BaselineJitterMs { get; set; }
    public double BaselineThroughputMbps { get; set; }
    public double BaselineBufferbloatMs { get; set; }
    public double CurrentScore { get; set; }
    public double CurrentLatencyMs { get; set; }
    public double CurrentJitterMs { get; set; }
    public double CurrentThroughputMbps { get; set; }
    public double CurrentPacketLoss { get; set; }
    public string CurrentBufferbloatGrade { get; set; } = "A";
    public List<ExperimentRecord> CompletedExperiments { get; set; } = [];
}