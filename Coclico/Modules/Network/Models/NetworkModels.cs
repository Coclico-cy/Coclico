namespace Coclico.Models.Network;

public enum NetworkAdapterType
{
    Ethernet,
    Wireless80211,
    Other
}

public enum NetworkTuningCategory
{
    TcpStack,
    Adapter
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
}

public sealed class TcpGlobalSettings
{
    public string AutoTuningLevel { get; set; } = "normal";
    public string ScalingHeuristics { get; set; } = "disabled";
    public string Rss { get; set; } = "enabled";
    public string Rsc { get; set; } = "disabled";
    public string EcnCapability { get; set; } = "disabled";
    public string Timestamps { get; set; } = "disabled";
    public string CongestionProvider { get; set; } = "ctcp";
}

public sealed class NetworkTuningParameter
{
    public required string Id { get; init; }
    public required string NameKey { get; init; }
    public required string DescriptionKey { get; init; }
    public required NetworkTuningCategory Category { get; init; }
    public required string TargetValue { get; init; }
    public string CurrentValue { get; set; } = "--";
    public bool IsOptimal { get; set; }
    public bool ApplyFailed { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CategoryLabel { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public string StatusColorHex { get; set; } = "#94A3B8";
    public string CurrentToTarget => $"{CurrentValue} → {TargetValue}";
}

public sealed class NetworkTestReport
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public double DownloadMbps { get; set; }
    public double UploadMbps { get; set; }
    public double LatencyMs { get; set; }
    public double JitterMs { get; set; }
    public double PacketLossPercent { get; set; }
    public int DownloadRuns { get; set; }
    public int UploadRuns { get; set; }
    public int PingSamples { get; set; }
    public bool DownloadSucceeded { get; set; }
    public bool UploadSucceeded { get; set; }

    public string DownloadDisplay => DownloadSucceeded ? $"{DownloadMbps:F1} Mbps" : "Indisponible";
    public string UploadDisplay => UploadSucceeded ? $"{UploadMbps:F1} Mbps" : "Indisponible";
    public string LatencyDisplay => LatencyMs > 0 ? $"{LatencyMs:F1} ms" : "Indisponible";
    public string JitterDisplay => JitterMs >= 0 ? $"±{JitterMs:F1} ms" : "Indisponible";
    public string PacketLossDisplay => $"{PacketLossPercent:F1} %";
}

public sealed class NetworkTuningReport
{
    public NetworkTestReport? Before { get; set; }
    public NetworkTestReport? After { get; set; }
    public int AppliedCount { get; set; }
    public int AlreadyOptimalCount { get; set; }
    public int FailedCount { get; set; }
    public bool RolledBack { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public sealed class NetworkTuningBackup
{
    public DateTime CapturedAt { get; set; }
    public string AdapterId { get; set; } = string.Empty;
    public Dictionary<string, string?> Values { get; set; } = [];
}
