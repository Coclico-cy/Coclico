namespace Coclico.Models.Network;

public enum NetworkAdapterType
{
    Ethernet,
    Wireless80211,
    Other
}

public enum NetworkOptimizationPreset
{
    Fast,
    GamingUltra,
    MaxThroughput,
    AutoTuned
}

public sealed class NetworkAdapterInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public NetworkAdapterType AdapterType { get; init; }
    public string Status { get; init; } = "Inconnu";
    public bool IsActive { get; init; }
    public bool IsPhysical { get; init; } = true;
    public long SpeedBitsPerSecond { get; init; }
    public string SpeedDisplay => SpeedBitsPerSecond switch
    {
        >= 1_000_000_000 => $"{SpeedBitsPerSecond / 1_000_000_000.0:F1} Gbps",
        >= 1_000_000 => $"{SpeedBitsPerSecond / 1_000_000.0:F0} Mbps",
        >= 1_000 => $"{SpeedBitsPerSecond / 1_000.0:F0} Kbps",
        _ => "Non connecté"
    };

    public string MacAddress { get; init; } = string.Empty;
    public string IPv4Address { get; init; } = string.Empty;
    public string GatewayAddress { get; init; } = string.Empty;
    public List<string> DnsServers { get; init; } = [];
    public int Mtu { get; set; } = 1500;
    public string DriverDescription { get; init; } = string.Empty;
    public string DriverVersion { get; init; } = string.Empty;
    public string PnpInstanceId { get; init; } = string.Empty;

    public string TypeIcon => AdapterType switch
    {
        NetworkAdapterType.Wireless80211 => "Wifi424",
        NetworkAdapterType.Ethernet => "Router24",
        _ => "Globe24"
    };

    public string TypeLabel => AdapterType switch
    {
        NetworkAdapterType.Wireless80211 => "Wi-Fi (Sans-fil)",
        NetworkAdapterType.Ethernet => "Ethernet (Filaire)",
        _ => "Autre Réseau"
    };

    public Dictionary<string, string> AdvancedProperties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TcpGlobalSettings
{
    public string AutoTuningLevel { get; set; } = "normal";
    public string Rss { get; set; } = "enabled";
    public string Rsc { get; set; } = "disabled";
    public string EcnCapability { get; set; } = "disabled";
    public string Timestamps { get; set; } = "disabled";
    public string CongestionProvider { get; set; } = "ctcp";
    public uint NetworkThrottlingIndex { get; set; } = 0xFFFFFFFF;
    public uint SystemResponsiveness { get; set; } = 0;
    public int TcpAckFrequency { get; set; } = 1;
    public int TcpNoDelay { get; set; } = 1;

    public bool IsNagleDisabled
    {
        get => TcpNoDelay == 1;
        set => TcpNoDelay = value ? 1 : 0;
    }

    public bool IsAckImmediate
    {
        get => TcpAckFrequency == 1;
        set => TcpAckFrequency = value ? 1 : 2;
    }

    public bool IsMultimediaThrottlingDisabled
    {
        get => NetworkThrottlingIndex == 0xFFFFFFFF;
        set => NetworkThrottlingIndex = value ? 0xFFFFFFFF : 10u;
    }

    public bool IsGamingResponsivenessPriority
    {
        get => SystemResponsiveness == 0;
        set => SystemResponsiveness = value ? 0u : 20u;
    }

    public bool IsRssEnabled
    {
        get => string.Equals(Rss, "enabled", StringComparison.OrdinalIgnoreCase);
        set => Rss = value ? "enabled" : "disabled";
    }

    public bool IsRscDisabled
    {
        get => string.Equals(Rsc, "disabled", StringComparison.OrdinalIgnoreCase);
        set => Rsc = value ? "disabled" : "enabled";
    }

    public bool IsTimestampsDisabled
    {
        get => string.Equals(Timestamps, "disabled", StringComparison.OrdinalIgnoreCase);
        set => Timestamps = value ? "disabled" : "enabled";
    }

    public bool IsEcnEnabled
    {
        get => string.Equals(EcnCapability, "enabled", StringComparison.OrdinalIgnoreCase);
        set => EcnCapability = value ? "enabled" : "disabled";
    }
}

public sealed class NicHardwareSettings
{
    public string AdapterIdOrGuid { get; set; } = string.Empty;
    public string AdapterName { get; set; } = string.Empty;
    public bool IsWireless { get; set; }

    public bool InterruptModerationDisabled { get; set; } = true;
    public bool FlowControlDisabled { get; set; } = true;
    public bool LsoV2IPv4Disabled { get; set; } = true;
    public bool LsoV2IPv6Disabled { get; set; } = true;
    public bool EnergyEfficientEthernetDisabled { get; set; } = true;
    public int ReceiveBuffers { get; set; } = 4096;
    public int TransmitBuffers { get; set; } = 4096;

    public bool PacketCoalescingDisabled { get; set; } = true;
    public bool ThroughputBoosterEnabled { get; set; } = true;
    public int RoamAggressiveness { get; set; } = 3;
}

public sealed class CalibrationStepProgress
{
    public int StepIndex { get; set; }
    public int TotalSteps { get; set; } = 8;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "En attente";
    public string MetricValue { get; set; } = string.Empty;
    public double ProgressPercentage => TotalSteps > 0 ? StepIndex / (double)TotalSteps * 100.0 : 0;
}

public sealed class DnsBenchmarkResult
{
    public required string ProviderName { get; init; }
    public required string PrimaryIp { get; init; }
    public required string SecondaryIp { get; init; }
    public double LatencyMs { get; set; }
    public double JitterMs { get; set; }
    public double PacketLossPercent { get; set; }
    public bool IsFastest { get; set; }
    public bool Success { get; set; }

    public string DisplayLatency => Success ? $"{LatencyMs:F1} ms (±{JitterMs:F1}ms)" : "Injoignable";
}

public sealed class MtuDetectionResult
{
    public int OptimalMtu { get; set; } = 1500;
    public int MaxPayloadWithoutFragmentation { get; set; } = 1472;
    public bool IsOptimized { get; set; }
    public string Details { get; set; } = string.Empty;
}

public sealed class NetworkBenchmarkReport
{
    public double BaselinePingMs { get; set; }
    public double BaselineJitterMs { get; set; }
    public double BaselinePacketLoss { get; set; }
    public double BaselineBufferbloatMs { get; set; }

    public double OptimizedPingMs { get; set; }
    public double OptimizedJitterMs { get; set; }
    public double OptimizedPacketLoss { get; set; }
    public double OptimizedBufferbloatMs { get; set; }

    public int OptimalMtu { get; set; } = 1500;
    public string SelectedCongestionProvider { get; set; } = "ctcp";
    public string FastestDnsProvider { get; set; } = string.Empty;

    public int HealthScoreBefore { get; set; } = 65;
    public int HealthScoreAfter { get; set; } = 98;

    public double GainPercentage => BaselinePingMs > 0 && OptimizedPingMs > 0
        ? Math.Max(0, (BaselinePingMs - OptimizedPingMs) / BaselinePingMs * 100.0)
        : 0;

    public int AppliedTweaksCount { get; set; }
    public List<string> AppliedModifications { get; } = [];
    public string SummaryText { get; set; } = string.Empty;
    public bool IsComplete { get; set; }
}