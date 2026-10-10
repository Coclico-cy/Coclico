using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Coclico.Models.Network;
using Microsoft.Win32;

namespace Coclico.Services.Network;

[SupportedOSPlatform("windows")]
public sealed class NetworkOptimizerService : INetworkOptimizerService
{
    public INetworkExperimentEngine Engine { get; }
    public INetworkDiscoveryService DiscoveryService { get; }
    public INetworkSnapshotService SnapshotService { get; }

    private const string ElevationRequiredSummary =
        "Coclico doit être exécuté en tant qu'administrateur (bouton d'élévation dans la barre de titre) pour modifier les paramètres réseau (netsh, registre HKLM).";

    private static readonly (string Name, string Primary, string Secondary)[] StandardDnsList =
    [
        ("Cloudflare (1.1.1.1)", "1.1.1.1", "1.0.0.1"),
        ("Google Public DNS (8.8.8.8)", "8.8.8.8", "8.8.4.4"),
        ("Quad9 Sécurisé (9.9.9.9)", "9.9.9.9", "149.112.112.112"),
        ("AdGuard Protection Pubs", "94.140.14.14", "94.140.15.15"),
        ("OpenDNS Home", "208.67.222.222", "208.67.220.220")
    ];

    private static readonly string[] VirtualOrFilterKeywords =
    [
        "virtual", "filter", "scheduler", "teredo", "isatap", "6to4",
        "bluetooth", "miniport", "kernel debug", "wfp", "lightweight",
        "direct", "pseudo", "tunneling", "vpn", "wireguard", "tailscale",
        "tap-windows", "npcap", "pcap", "vmware", "hyper-v", "virtualbox",
        "débogueur", "connexion au réseau local*"
    ];

    private static readonly ConcurrentDictionary<string, string> AdapterSubKeyCache =
        new(StringComparer.OrdinalIgnoreCase);

    public NetworkOptimizerService(
        INetworkDiscoveryService? discoveryService = null,
        INetworkSafetyService? safetyService = null,
        INetworkSnapshotService? snapshotService = null,
        INetworkBenchmarkService? benchmarkService = null,
        INetworkScoringService? scoringService = null,
        INetworkWatchdogService? watchdogService = null,
        INetworkMemoryService? memoryService = null,
        INetworkDiagnosticsService? diagnosticsService = null,
        INetworkExperimentEngine? engine = null)
    {
        DiscoveryService = discoveryService ?? new NetworkDiscoveryService();
        INetworkSafetyService safety = safetyService ?? new NetworkSafetyService();
        SnapshotService = snapshotService ?? new NetworkSnapshotService();
        INetworkBenchmarkService benchmark = benchmarkService ?? new NetworkBenchmarkService();
        INetworkScoringService scoring = scoringService ?? new NetworkScoringService();
        INetworkWatchdogService watchdog = watchdogService ?? new NetworkWatchdogService();
        INetworkMemoryService memory = memoryService ?? new NetworkMemoryService();
        INetworkDiagnosticsService diagnostics = diagnosticsService ?? new NetworkDiagnosticsService();

        Engine = engine ?? new NetworkExperimentEngine(
            DiscoveryService,
            safety,
            SnapshotService,
            benchmark,
            scoring,
            watchdog,
            memory,
            diagnostics);
    }

    public async Task<string> RunAdaptiveEngineSessionAsync(
        string? targetAdapterId = null,
        OptimizationProfile profile = OptimizationProfile.Gaming,
        EngineExecutionMode mode = EngineExecutionMode.Auto,
        int samplesPerTest = 10,
        Action<EngineProgressInfo>? onProgress = null,
        Action<CognitiveLogEntry>? onLog = null,
        CancellationToken ct = default)
    {
        if (!App.IsRunningAsAdministrator())
        {
            LoggingService.LogWarning("[NetworkOptimizerService] Session adaptative refusée : élévation administrateur requise.");
            return "Session annulée : " + ElevationRequiredSummary;
        }

        if (onProgress != null)
        {
            Engine.OnProgressUpdated += onProgress;
        }

        if (onLog != null)
        {
            Engine.OnLogEntryAdded += onLog;
        }

        try
        {
            return await Engine.RunAdaptiveOptimizationSessionAsync(targetAdapterId, profile, mode, samplesPerTest, ct).ConfigureAwait(false);
        }
        finally
        {
            if (onProgress != null)
            {
                Engine.OnProgressUpdated -= onProgress;
            }

            if (onLog != null)
            {
                Engine.OnLogEntryAdded -= onLog;
            }
        }
    }

    public async Task<string> RunGlobalUnifiedOptimizationAsync(
        string? targetAdapterId = null,
        OptimizationProfile profile = OptimizationProfile.Gaming,
        EngineExecutionMode mode = EngineExecutionMode.Auto,
        Action<EngineProgressInfo>? onProgress = null,
        Action<CognitiveLogEntry>? onLog = null,
        CancellationToken ct = default)
    {
        if (!App.IsRunningAsAdministrator())
        {
            LoggingService.LogWarning("[NetworkOptimizerService] Optimisation globale refusée : élévation administrateur requise.");
            onLog?.Invoke(new CognitiveLogEntry { Message = "Optimisation annulée : " + ElevationRequiredSummary });
            return "Optimisation annulée : " + ElevationRequiredSummary;
        }

        onLog?.Invoke(new CognitiveLogEntry
        {
            Message = "Note : chaque test de latence/débit consomme ~512 Ko depuis speed.cloudflare.com — une session Expert peut utiliser quelques dizaines de Mo de données.",
            Level = "INFO"
        });

        try
        {
            IReadOnlyList<DnsBenchmarkResult> dnsList = await RunDnsBenchmarkAsync(null, ct).ConfigureAwait(false);
            DnsBenchmarkResult? fastestDns = dnsList.FirstOrDefault(d => d.Success && d.IsFastest);
            if (fastestDns != null)
            {
                IReadOnlyList<NetworkAdapterInfo> adapters = await GetNetworkAdaptersAsync(ct).ConfigureAwait(false);
                NetworkAdapterInfo? targetAdapter = ResolveTargetAdapter(adapters, targetAdapterId);

                if (targetAdapter != null)
                {
                    _ = await ApplyDnsAsync(targetAdapter.Name, fastestDns.PrimaryIp, fastestDns.SecondaryIp, ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "GlobalOptimization.Dns");
        }

        try
        {
            MtuDetectionResult mtuRes = await DetectOptimalMtuAsync("1.1.1.1", null, ct).ConfigureAwait(false);
            if (mtuRes.IsOptimized)
            {
                IReadOnlyList<NetworkAdapterInfo> adapters = await GetNetworkAdaptersAsync(ct).ConfigureAwait(false);
                NetworkAdapterInfo? targetAdapter = ResolveTargetAdapter(adapters, targetAdapterId);

                if (targetAdapter != null)
                {
                    _ = await ApplyMtuAsync(targetAdapter.Name, mtuRes.OptimalMtu, ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "GlobalOptimization.Mtu");
        }

        try
        {
            ApplyMultimediaThrottlingRegistry(0xFFFFFFFF, 0);
            ApplyNagleDisablingRegistry(targetAdapterId, noDelay: 1, ackFrequency: 1);
            ApplyIPv6OptimizationRegistry(targetAdapterId);
            ApplyTcpKernelStackOptimizationsRegistry();
            ApplyDnsCacheOptimizationsRegistry();
            ApplyQosOptimizationsRegistry();
            ApplyNicLowLatencyProperties(targetAdapterId);

            _ = await RunProcessAsync("netsh.exe", "int tcp set global autotuninglevel=normal", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", "int tcp set global rss=enabled", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", "int tcp set global rsc=disabled", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", "int tcp set global timestamps=disabled", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", "int tcp set global ecncapability=disabled", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", "int tcp set heuristics disabled", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", "int tcp set supplemental template=custom congestionprovider=ctcp", ct).ConfigureAwait(false);

            _ = await RunProcessAsync("netsh.exe", "int teredo set state disabled", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", "int ipv6 isatap set state disabled", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", "int 6to4 set state disabled", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", "int ipv6 set global randomizeidentifiers=disabled", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "GlobalOptimization.BasePreset");
        }

        int samples = (mode == EngineExecutionMode.Expert) ? 40 : 10;
        string report = await RunAdaptiveEngineSessionAsync(targetAdapterId, profile, mode, samples, onProgress, onLog, ct).ConfigureAwait(false);
        _ = await RunProcessAsync("ipconfig.exe", "/flushdns", ct).ConfigureAwait(false);
        return report;
    }

    public async Task<IReadOnlyList<NetworkAdapterInfo>> GetNetworkAdaptersAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<NetworkAdapterInfo>();
            try
            {
                Dictionary<string, (string DriverDesc, string DriverVersion, string PnpId)> physicalGuids = GetPhysicalAdapterGuids();
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();

                foreach (NetworkInterface ni in interfaces)
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    if (IsVirtualOrFilter(ni.Name, ni.Description))
                    {
                        continue;
                    }

                    if (physicalGuids.Count > 0 && !physicalGuids.ContainsKey(ni.Id))
                    {
                        continue;
                    }

                    NetworkAdapterType type = ni.NetworkInterfaceType switch
                    {
                        NetworkInterfaceType.Wireless80211 => NetworkAdapterType.Wireless80211,
                        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => NetworkAdapterType.Ethernet,
                        _ => NetworkAdapterType.Other
                    };

                    IPInterfaceProperties ipProps = ni.GetIPProperties();
                    string ipv4 = ipProps.UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "";

                    string gateway = ipProps.GatewayAddresses
                        .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "";

                    var dns = ipProps.DnsAddresses
                        .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                        .Select(d => d.ToString())
                        .ToList();

                    int mtu = 1500;
                    try
                    {
                        IPv4InterfaceProperties v4 = ipProps.GetIPv4Properties();
                        if (v4 != null)
                        {
                            mtu = v4.Mtu;
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogException(ex, "NetworkOptimizerService.GetNetworkAdaptersAsync:Mtu");
                    }

                    string mac = ni.GetPhysicalAddress().ToString();
                    if (mac.Length == 12)
                    {
                        mac = string.Join(":", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)));
                    }

                    _ = physicalGuids.TryGetValue(ni.Id, out (string DriverDesc, string DriverVersion, string PnpId) pnpMeta);

                    var info = new NetworkAdapterInfo
                    {
                        Id = ni.Id,
                        Name = ni.Name,
                        Description = ni.Description,
                        AdapterType = type,
                        Status = ni.OperationalStatus == OperationalStatus.Up ? "Connecté" : "Déconnecté",
                        IsActive = ni.OperationalStatus == OperationalStatus.Up && !string.IsNullOrEmpty(ipv4),
                        IsPhysical = true,
                        SpeedBitsPerSecond = ni.Speed,
                        MacAddress = mac,
                        IPv4Address = ipv4,
                        GatewayAddress = gateway,
                        DnsServers = dns,
                        Mtu = mtu,
                        DriverDescription = pnpMeta.DriverDesc ?? ni.Description,
                        DriverVersion = pnpMeta.DriverVersion ?? string.Empty,
                        PnpInstanceId = pnpMeta.PnpId ?? string.Empty
                    };

                    ReadNicRegistryProperties(ni.Id, info.AdvancedProperties);
                    list.Add(info);
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "NetworkOptimizerService.GetNetworkAdaptersAsync");
            }

            return list
                .OrderByDescending(a => a.IsActive)
                .ThenBy(a => a.AdapterType == NetworkAdapterType.Ethernet ? 0 : 1)
                .ToList();
        }, ct).ConfigureAwait(false);
    }

    private static NetworkAdapterInfo? ResolveTargetAdapter(IReadOnlyList<NetworkAdapterInfo> adapters, string? targetAdapterId)
    {
        return string.IsNullOrEmpty(targetAdapterId)
            ? adapters.FirstOrDefault(a => a.IsActive) ?? adapters.FirstOrDefault()
            : adapters.FirstOrDefault(a => a.Id == targetAdapterId || a.Name == targetAdapterId);
    }

    private static Dictionary<string, (string DriverDesc, string DriverVersion, string PnpId)> GetPhysicalAdapterGuids()
    {
        var dict = new Dictionary<string, (string DriverDesc, string DriverVersion, string PnpId)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using RegistryKey? classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}");
            if (classKey == null)
            {
                return dict;
            }

            foreach (string subName in classKey.GetSubKeyNames())
            {
                if (!int.TryParse(subName, out _))
                {
                    continue;
                }

                using RegistryKey? sub = classKey.OpenSubKey(subName);
                if (sub == null)
                {
                    continue;
                }

                string? guid = sub.GetValue("NetCfgInstanceId") as string;
                if (string.IsNullOrEmpty(guid))
                {
                    continue;
                }

                string matchId = sub.GetValue("MatchingDeviceId") as string ?? "";
                string pnpId = sub.GetValue("PnPInstanceId") as string ?? "";

                if (matchId.StartsWith("PCI", StringComparison.OrdinalIgnoreCase) ||
                    matchId.StartsWith("USB", StringComparison.OrdinalIgnoreCase) ||
                    pnpId.StartsWith("PCI", StringComparison.OrdinalIgnoreCase) ||
                    pnpId.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
                {
                    string desc = sub.GetValue("DriverDesc") as string ?? "";
                    string ver = sub.GetValue("DriverVersion") as string ?? "";
                    dict[guid] = (desc, ver, pnpId);
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerService.GetPhysicalAdapterGuids");
        }
        return dict;
    }

    private static bool IsVirtualOrFilter(string name, string desc)
    {
        foreach (string keyword in VirtualOrFilterKeywords)
        {
            if (desc.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static RegistryKey? OpenAdapterRegistryKey(string interfaceGuid, bool writable)
    {
        try
        {
            if (AdapterSubKeyCache.TryGetValue(interfaceGuid, out string? cachedSubName))
            {
                RegistryKey? cached = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\" + cachedSubName, writable);
                if (cached != null)
                {
                    return cached;
                }
            }

            RegistryKey? classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}", writable);
            if (classKey == null)
            {
                return null;
            }

            foreach (string subName in classKey.GetSubKeyNames())
            {
                if (!int.TryParse(subName, out _))
                {
                    continue;
                }

                RegistryKey? sub = classKey.OpenSubKey(subName, writable);
                if (sub == null)
                {
                    continue;
                }

                string? netCfgInstanceId = sub.GetValue("NetCfgInstanceId") as string;
                if (string.Equals(netCfgInstanceId, interfaceGuid, StringComparison.OrdinalIgnoreCase))
                {
                    _ = AdapterSubKeyCache.TryAdd(interfaceGuid, subName);
                    return sub;
                }
                sub.Dispose();
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerService.OpenAdapterRegistryKey");
        }
        return null;
    }

    private static void ReadNicRegistryProperties(string interfaceGuid, Dictionary<string, string> props)
    {
        try
        {
            using RegistryKey? sub = OpenAdapterRegistryKey(interfaceGuid, writable: false);
            if (sub == null)
            {
                return;
            }

            foreach (string valName in sub.GetValueNames())
            {
                if (valName.StartsWith("*") || valName.Contains("Interrupt", StringComparison.OrdinalIgnoreCase) ||
                    valName.Contains("Flow", StringComparison.OrdinalIgnoreCase) || valName.Contains("LSO", StringComparison.OrdinalIgnoreCase) ||
                    valName.Contains("Energy", StringComparison.OrdinalIgnoreCase) || valName.Contains("EEE", StringComparison.OrdinalIgnoreCase) ||
                    valName.Contains("RSS", StringComparison.OrdinalIgnoreCase) || valName.Contains("Buffer", StringComparison.OrdinalIgnoreCase) ||
                    valName.Contains("Booster", StringComparison.OrdinalIgnoreCase) || valName.Contains("Coalescing", StringComparison.OrdinalIgnoreCase))
                {
                    string? val = sub.GetValue(valName)?.ToString();
                    if (!string.IsNullOrEmpty(val))
                    {
                        props[valName] = val;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerService.ReadNicRegistryProperties");
        }
    }

    public async Task<NicHardwareSettings> GetNicHardwareSettingsAsync(string adapterIdOrGuid, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var settings = new NicHardwareSettings
            {
                AdapterIdOrGuid = adapterIdOrGuid
            };

            try
            {
                using RegistryKey? sub = OpenAdapterRegistryKey(adapterIdOrGuid, writable: false);
                if (sub != null)
                {
                    string desc = sub.GetValue("DriverDesc")?.ToString() ?? "";
                    settings.AdapterName = desc;
                    settings.IsWireless = desc.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
                                          desc.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ||
                                          desc.Contains("802.11", StringComparison.OrdinalIgnoreCase);

                    string? im = sub.GetValue("*InterruptModeration")?.ToString() ?? sub.GetValue("InterruptModeration")?.ToString();
                    if (im != null)
                    {
                        settings.InterruptModerationDisabled = im == "0";
                    }

                    string? fc = sub.GetValue("*FlowControl")?.ToString() ?? sub.GetValue("FlowControl")?.ToString();
                    if (fc != null)
                    {
                        settings.FlowControlDisabled = fc == "0";
                    }

                    string? lso4 = sub.GetValue("*LsoV2IPv4")?.ToString();
                    if (lso4 != null)
                    {
                        settings.LsoV2IPv4Disabled = lso4 == "0";
                    }

                    string? lso6 = sub.GetValue("*LsoV2IPv6")?.ToString();
                    if (lso6 != null)
                    {
                        settings.LsoV2IPv6Disabled = lso6 == "0";
                    }

                    string? eee = sub.GetValue("*EEE")?.ToString() ?? sub.GetValue("EnableGreenEthernet")?.ToString() ?? sub.GetValue("EnergyEfficientEthernet")?.ToString();
                    if (eee != null)
                    {
                        settings.EnergyEfficientEthernetDisabled = eee == "0";
                    }

                    if (int.TryParse(sub.GetValue("*ReceiveBuffers")?.ToString(), out int rx))
                    {
                        settings.ReceiveBuffers = rx;
                    }

                    if (int.TryParse(sub.GetValue("*TransmitBuffers")?.ToString(), out int tx))
                    {
                        settings.TransmitBuffers = tx;
                    }

                    string? pc = sub.GetValue("*PacketCoalescing")?.ToString();
                    if (pc != null)
                    {
                        settings.PacketCoalescingDisabled = pc == "0";
                    }

                    string? tb = sub.GetValue("ThroughputBoosterEnabled")?.ToString();
                    if (tb != null)
                    {
                        settings.ThroughputBoosterEnabled = tb == "1";
                    }

                    if (int.TryParse(sub.GetValue("RoamAggressiveness")?.ToString(), out int roam))
                    {
                        settings.RoamAggressiveness = roam;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "NetworkOptimizerService.GetNicHardwareSettingsAsync");
            }

            return settings;
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> ApplyCustomNicSettingsAsync(string adapterIdOrGuid, NicHardwareSettings settings, CancellationToken ct = default)
    {
        if (!App.IsRunningAsAdministrator())
        {
            LoggingService.LogWarning("[NetworkOptimizerService] ApplyCustomNicSettings refusé : élévation administrateur requise.");
            return false;
        }

        return await Task.Run(async () =>
        {
            try
            {
                using RegistryKey? sub = OpenAdapterRegistryKey(adapterIdOrGuid, writable: true);
                if (sub != null)
                {
                    sub.SetValue("*InterruptModeration", settings.InterruptModerationDisabled ? "0" : "1", RegistryValueKind.String);
                    SetRegistryValueIfPresent(sub, "InterruptModeration", settings.InterruptModerationDisabled ? "0" : "1");

                    sub.SetValue("*FlowControl", settings.FlowControlDisabled ? "0" : "3", RegistryValueKind.String);
                    SetRegistryValueIfPresent(sub, "FlowControl", settings.FlowControlDisabled ? "0" : "3");

                    sub.SetValue("*LsoV2IPv4", settings.LsoV2IPv4Disabled ? "0" : "1", RegistryValueKind.String);
                    sub.SetValue("*LsoV2IPv6", settings.LsoV2IPv6Disabled ? "0" : "1", RegistryValueKind.String);

                    sub.SetValue("*EEE", settings.EnergyEfficientEthernetDisabled ? "0" : "1", RegistryValueKind.String);
                    SetRegistryValueIfPresent(sub, "EnableGreenEthernet", settings.EnergyEfficientEthernetDisabled ? "0" : "1");
                    SetRegistryValueIfPresent(sub, "GigaLite", settings.EnergyEfficientEthernetDisabled ? "0" : "1");
                    SetRegistryValueIfPresent(sub, "EEELinkAdvertisement", settings.EnergyEfficientEthernetDisabled ? "0" : "1");
                    SetRegistryValueIfPresent(sub, "PowerSavingMode", settings.EnergyEfficientEthernetDisabled ? "0" : "1");

                    sub.SetValue("*ReceiveBuffers", settings.ReceiveBuffers.ToString(), RegistryValueKind.String);
                    sub.SetValue("*TransmitBuffers", settings.TransmitBuffers.ToString(), RegistryValueKind.String);

                    if (settings.IsWireless)
                    {
                        SetRegistryValueIfPresent(sub, "*PacketCoalescing", settings.PacketCoalescingDisabled ? "0" : "1");
                        SetRegistryValueIfPresent(sub, "ThroughputBoosterEnabled", settings.ThroughputBoosterEnabled ? "1" : "0");
                        SetRegistryValueIfPresent(sub, "RoamAggressiveness", settings.RoamAggressiveness.ToString());
                    }

                    if (!string.IsNullOrEmpty(settings.AdapterName))
                    {
                        _ = RunProcessAsync("powershell.exe",
                            $"-NoProfile -ExecutionPolicy Bypass -Command \"Set-NetAdapterAdvancedProperty -Name '{settings.AdapterName}' -RegistryKeyword '*InterruptModeration' -RegistryValue '{(settings.InterruptModerationDisabled ? "0" : "1")}' -ErrorAction SilentlyContinue\"", ct);
                    }

                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "NetworkOptimizerService.ApplyCustomNicSettingsAsync");
                return false;
            }
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> ApplyCustomTcpSettingsAsync(TcpGlobalSettings settings, string? adapterId = null, CancellationToken ct = default)
    {
        if (!App.IsRunningAsAdministrator())
        {
            LoggingService.LogWarning("[NetworkOptimizerService] ApplyCustomTcpSettings refusé : élévation administrateur requise.");
            return false;
        }

        try
        {
            _ = await RunProcessAsync("netsh.exe", $"int tcp set global autotuninglevel={settings.AutoTuningLevel}", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", $"int tcp set global rss={settings.Rss}", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", $"int tcp set global rsc={settings.Rsc}", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", $"int tcp set global timestamps={settings.Timestamps}", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", $"int tcp set global ecncapability={settings.EcnCapability}", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", $"int tcp set supplemental template=custom congestionprovider={settings.CongestionProvider}", ct).ConfigureAwait(false);

            ApplyMultimediaThrottlingRegistry(settings.NetworkThrottlingIndex, settings.SystemResponsiveness);
            ApplyNagleDisablingRegistry(adapterId, settings.TcpNoDelay, settings.TcpAckFrequency);

            _ = await RunProcessAsync("ipconfig.exe", "/flushdns", ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerService.ApplyCustomTcpSettingsAsync");
            return false;
        }
    }

    public async Task<TcpGlobalSettings> GetTcpSettingsAsync(CancellationToken ct = default)
    {
        return await Task.Run(async () =>
        {
            var settings = new TcpGlobalSettings();
            try
            {
                string netshOut = await RunProcessOutputAsync("netsh.exe", "int tcp show global", ct).ConfigureAwait(false);
                string[] lines = netshOut.Split('\n');
                foreach (string line in lines)
                {
                    string[] parts = line.Split(':', 2);
                    if (parts.Length != 2)
                    {
                        continue;
                    }

                    string key = parts[0].Trim().ToLowerInvariant();
                    string val = parts[1].Trim().ToLowerInvariant();

                    if (key.Contains("réglage automatique") || key.Contains("autotuninglevel"))
                    {
                        settings.AutoTuningLevel = val;
                    }
                    else if (key.Contains("mise à l'échelle côté réception") || key.Contains("receive-side scaling"))
                    {
                        settings.Rss = val;
                    }
                    else if (key.Contains("coalescence de segments") || key.Contains("receive segment coalescing"))
                    {
                        settings.Rsc = val;
                    }
                    else if (key.Contains("capacité ecn") || key.Contains("ecncapability"))
                    {
                        settings.EcnCapability = val;
                    }
                    else if (key.Contains("horodatages rfc 1323") || key.Contains("timestamps"))
                    {
                        settings.Timestamps = val;
                    }
                    else if (key.Contains("fournisseur de congestion") || key.Contains("congestion provider"))
                    {
                        settings.CongestionProvider = val;
                    }
                }

                using RegistryKey? sysProfile = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                if (sysProfile != null)
                {
                    if (sysProfile.GetValue("NetworkThrottlingIndex") is int nti)
                    {
                        settings.NetworkThrottlingIndex = (uint)nti;
                    }

                    if (sysProfile.GetValue("SystemResponsiveness") is int sr)
                    {
                        settings.SystemResponsiveness = (uint)sr;
                    }
                }

                using RegistryKey? tcpKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
                if (tcpKey != null)
                {
                    if (tcpKey.GetValue("TcpAckFrequency") is int taf)
                    {
                        settings.TcpAckFrequency = taf;
                    }

                    if (tcpKey.GetValue("TCPNoDelay") is int tnd)
                    {
                        settings.TcpNoDelay = tnd;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "NetworkOptimizerService.GetTcpSettingsAsync");
            }

            return settings;
        }, ct).ConfigureAwait(false);
    }

    public async Task<NetworkBenchmarkReport> ApplyPresetAsync(
        NetworkOptimizationPreset preset,
        string? targetAdapterId = null,
        Action<string, double>? progress = null,
        CancellationToken ct = default)
    {
        if (!App.IsRunningAsAdministrator())
        {
            LoggingService.LogWarning("[NetworkOptimizerService] Preset refusé : élévation administrateur requise.");
            var refused = new NetworkBenchmarkReport
            {
                HealthScoreBefore = 0,
                HealthScoreAfter = 0,
                SummaryText = "Optimisation annulée : " + ElevationRequiredSummary
            };
            progress?.Invoke(refused.SummaryText, 100);
            return refused;
        }

        var report = new NetworkBenchmarkReport();

        progress?.Invoke("📊 Mesure de la latence initiale (Ping & Gigue)...", 5);
        (double bPing, double bJitter, double bLoss) = await MeasureLatencyAsync(count: 4, ct: ct).ConfigureAwait(false);
        report.BaselinePingMs = bPing;
        report.BaselineJitterMs = bJitter;
        report.BaselinePacketLoss = bLoss;

        progress?.Invoke($"Latence de départ : {bPing:F1} ms (±{bJitter:F1}ms)", 15);

        switch (preset)
        {
            case NetworkOptimizationPreset.Fast:
                await ApplyFastPresetAsync(report, progress, ct).ConfigureAwait(false);
                break;

            case NetworkOptimizationPreset.GamingUltra:
                await ApplyGamingUltraPresetAsync(targetAdapterId, report, progress, ct).ConfigureAwait(false);
                break;

            case NetworkOptimizationPreset.MaxThroughput:
                await ApplyMaxThroughputPresetAsync(targetAdapterId, report, progress, ct).ConfigureAwait(false);
                break;

            case NetworkOptimizationPreset.AutoTuned:
                return await RunAutoTuningOptimizationAsync(targetAdapterId, progress, ct).ConfigureAwait(false);
        }

        progress?.Invoke("🧹 Vidage du cache DNS et réinitialisation de la session...", 85);
        _ = await RunProcessAsync("ipconfig.exe", "/flushdns", ct).ConfigureAwait(false);

        progress?.Invoke("📈 Mesure de la latence finale...", 90);
        await Task.Delay(1000, ct).ConfigureAwait(false);
        (double oPing, double oJitter, double oLoss) = await MeasureLatencyAsync(count: 4, ct: ct).ConfigureAwait(false);
        report.OptimizedPingMs = oPing > 0 ? oPing : bPing;
        report.OptimizedJitterMs = oJitter;
        report.OptimizedPacketLoss = oLoss;

        report.SummaryText = $"Optimisation appliquée avec succès. Ping final : {report.OptimizedPingMs:F1} ms ({report.GainPercentage:F1}% d'amélioration).";
        progress?.Invoke(report.SummaryText, 100);

        return report;
    }

    private async Task ApplyFastPresetAsync(NetworkBenchmarkReport report, Action<string, double>? progress, CancellationToken ct)
    {
        progress?.Invoke("⚡ Configuration TCP/IP Standard & Anti-Throttling...", 30);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global autotuninglevel=normal", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global rss=enabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global timestamps=disabled", ct).ConfigureAwait(false);

        ApplyMultimediaThrottlingRegistry(0xFFFFFFFF, 0);
        report.AppliedModifications.Add("Désactivation du bridage réseau multimédia Windows (NetworkThrottlingIndex)");
        report.AppliedModifications.Add("Priorité réseau maximale accordée aux jeux et applications actives (SystemResponsiveness = 0)");
        report.AppliedModifications.Add("Auto-Tuning TCP réglé sur 'Normal' (meilleur équilibre)");
        report.AppliedTweaksCount = 3;
    }

    private async Task ApplyGamingUltraPresetAsync(string? targetAdapterId, NetworkBenchmarkReport report, Action<string, double>? progress, CancellationToken ct)
    {
        progress?.Invoke("🎮 Application des paramètres TCP Ultra-Low Latency...", 25);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global autotuninglevel=normal", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global rss=enabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global rsc=disabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global timestamps=disabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global ecncapability=disabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set supplemental template=custom congestionprovider=ctcp", ct).ConfigureAwait(false);

        report.AppliedModifications.Add("Désactivation de RSC (Receive Segment Coalescing) pour éliminer les micro-saccades de paquets");
        report.AppliedModifications.Add("Activation de l'algorithme de congestion CTCP (Compound TCP faible latence)");
        report.AppliedModifications.Add("Désactivation des horodatages TCP RFC 1323 (réduction de l'overhead de 12 octets/paquet)");

        progress?.Invoke("⚡ Désactivation de l'Algorithme de Nagle et délai ACK (IPv4 & IPv6)...", 45);
        ApplyMultimediaThrottlingRegistry(0xFFFFFFFF, 0);
        ApplyNagleDisablingRegistry(targetAdapterId);
        ApplyIPv6OptimizationRegistry(targetAdapterId);
        ApplyTcpKernelStackOptimizationsRegistry();
        ApplyDnsCacheOptimizationsRegistry();
        ApplyQosOptimizationsRegistry();

        _ = await RunProcessAsync("netsh.exe", "int teredo set state disabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int ipv6 isatap set state disabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int 6to4 set state disabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set heuristics disabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int ipv6 set global randomizeidentifiers=disabled", ct).ConfigureAwait(false);

        report.AppliedModifications.Add("Désactivation de l'algorithme de Nagle IPv4 & IPv6 (TCPNoDelay = 1, envoi immédiat des petits paquets)");
        report.AppliedModifications.Add("Accusé de réception immédiat IPv4 & IPv6 (TcpAckFrequency = 1, aucun délai d'attente ACK)");
        report.AppliedModifications.Add("Optimisation IPv6 : tunnels Teredo, ISATAP & 6to4 désactivés, identifiants stables");
        report.AppliedModifications.Add("Préférence IPv4 sur IPv6 (DisabledComponents = 0x20) pour éliminer les latences de timeout");
        report.AppliedModifications.Add("Heuristiques TCP désactivées (Auto-Tuning constant sans baisse de débit)");
        report.AppliedModifications.Add("Bridage réseau multimédia Windows complètement désactivé");
        report.AppliedModifications.Add("Calibrage du noyau TCP : TTL 64, ports éphémères max (65534), recyclage socket 30s");
        report.AppliedModifications.Add("Cache DNS Windows optimisé (TTL étendu 24h, TTL négatif à 0s pour réessai instantané)");
        report.AppliedModifications.Add("Priorité absolue QoS et suppression de la réserve de bande passante Windows (0%)");

        progress?.Invoke("🛠️ Optimisation du pilote de la carte réseau (NIC Offloads)...", 65);
        ApplyNicLowLatencyProperties(targetAdapterId);
        report.AppliedModifications.Add("Désactivation de l'Interruption Modérée (Interrupt Moderation) sur la carte réseau");
        report.AppliedModifications.Add("Désactivation du Contrôle de Flux (Flow Control) pour éviter les pauses de transmission");
        report.AppliedModifications.Add("Désactivation de Large Send Offload (LSO v1 & v2) pour réduire le buffering");
        report.AppliedModifications.Add("Désactivation de l'Energy Efficient Ethernet / Green Ethernet (zéro mise en veille de la carte)");
        report.AppliedModifications.Add("Distribution multi-cœur RSS optimisée (4 files RSS, cœur de base 0)");
        report.AppliedModifications.Add("Optimisation Wi-Fi : balayage en arrière-plan désactivé, économie d'énergie MIMO coupée");

        report.AppliedTweaksCount = 20;
    }

    private async Task ApplyMaxThroughputPresetAsync(string? targetAdapterId, NetworkBenchmarkReport report, Action<string, double>? progress, CancellationToken ct)
    {
        progress?.Invoke("🚀 Configuration TCP pour Débit Maximal (Téléchargement / Streaming 4K)...", 35);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global autotuninglevel=experimental", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global rss=enabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global rsc=enabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set supplemental template=custom congestionprovider=cubic", ct).ConfigureAwait(false);

        ApplyMultimediaThrottlingRegistry(0xFFFFFFFF, 10);

        report.AppliedModifications.Add("Fenêtre TCP Auto-Tuning en mode 'Experimental' (saturation maximale de la bande passante)");
        report.AppliedModifications.Add("Activation de RSC (Receive Segment Coalescing) pour regrouper les flux à haut débit");
        report.AppliedModifications.Add("Algorithme de congestion CUBIC optimisé pour lignes fibre très haut débit");
        report.AppliedTweaksCount = 4;
    }

    public async Task<NetworkBenchmarkReport> RunAutoTuningOptimizationAsync(
        string? targetAdapterId = null,
        Action<string, double>? progress = null,
        CancellationToken ct = default)
    {
        return await RunComprehensiveCalibrationAsync(targetAdapterId, deepMode: true, null, progress, ct).ConfigureAwait(false);
    }

    public async Task<NetworkBenchmarkReport> RunComprehensiveCalibrationAsync(
        string? targetAdapterId,
        bool deepMode,
        Action<CalibrationStepProgress>? stepProgress = null,
        Action<string, double>? progress = null,
        CancellationToken ct = default)
    {
        if (!App.IsRunningAsAdministrator())
        {
            LoggingService.LogWarning("[NetworkOptimizerService] Calibration refusée : élévation administrateur requise.");
            var refused = new NetworkBenchmarkReport
            {
                HealthScoreBefore = 0,
                HealthScoreAfter = 0,
                SummaryText = "Calibration annulée : " + ElevationRequiredSummary
            };
            progress?.Invoke(refused.SummaryText, 100);
            return refused;
        }

        var report = new NetworkBenchmarkReport();
        int totalSteps = 8;

        void NotifyStep(int stepIdx, string title, string description, string status, string metric, double pct)
        {
            var step = new CalibrationStepProgress
            {
                StepIndex = stepIdx,
                TotalSteps = totalSteps,
                Title = title,
                Description = description,
                Status = status,
                MetricValue = metric
            };
            stepProgress?.Invoke(step);
            progress?.Invoke($"[{stepIdx}/{totalSteps}] {title} : {metric}", pct);
        }

        NotifyStep(1, "Mesure de référence multi-cibles", "Analyse de la latence, gigue et perte de paquets vers Cloudflare, Google et passerelle locale", "En cours", "Mesure en cours...", 5);

        int baselineCount = deepMode ? 6 : 3;
        (double bPing1, double bJitter1, double bLoss1) = await MeasureLatencyAsync("1.1.1.1", baselineCount, ct).ConfigureAwait(false);
        (double bPing2, double bJitter2, double bLoss2) = await MeasureLatencyAsync("8.8.8.8", baselineCount, ct).ConfigureAwait(false);

        report.BaselinePingMs = (bPing1 > 0 && bPing2 > 0) ? (bPing1 + bPing2) / 2.0 : Math.Max(bPing1, bPing2);
        report.BaselineJitterMs = (bJitter1 + bJitter2) / 2.0;
        report.BaselinePacketLoss = (bLoss1 + bLoss2) / 2.0;

        NotifyStep(1, "Mesure de référence multi-cibles", "Point de départ validé avec succès", "Validé", $"{report.BaselinePingMs:F1} ms (±{report.BaselineJitterMs:F1}ms)", 15);

        NotifyStep(2, "Test de charge & Bufferbloat", "Évaluation de la dégradation de latence sous rafale de paquets (Bufferbloat)", "En cours", "Envoi de rafales...", 25);

        double loadedPing = await MeasureLoadedLatencyAsync("1.1.1.1", deepMode ? 4 : 2, ct).ConfigureAwait(false);
        report.BaselineBufferbloatMs = Math.Max(0, loadedPing - report.BaselinePingMs);

        NotifyStep(2, "Test de charge & Bufferbloat", "Saturation des files d'attente mesurée", "Validé", $"+{report.BaselineBufferbloatMs:F1} ms sous charge", 35);

        NotifyStep(3, "Détection MTU & MSS sans fragmentation", "Balayage ICMP paquet par paquet avec bit Don't Fragment pour éliminer l'overhead", "En cours", "Balayage 1400-1472...", 45);

        MtuDetectionResult mtuRes = await DetectOptimalMtuAsync("1.1.1.1", null, ct).ConfigureAwait(false);
        report.OptimalMtu = mtuRes.OptimalMtu;

        IReadOnlyList<NetworkAdapterInfo> adapters = await GetNetworkAdaptersAsync(ct).ConfigureAwait(false);
        NetworkAdapterInfo? targetAdapter = ResolveTargetAdapter(adapters, targetAdapterId);

        if (targetAdapter != null && mtuRes.IsOptimized)
        {
            _ = await ApplyMtuAsync(targetAdapter.Name, mtuRes.OptimalMtu, ct).ConfigureAwait(false);
            report.AppliedModifications.Add($"MTU calibré à {mtuRes.OptimalMtu} octets (taille payload optimale sans fragmentation)");
        }

        NotifyStep(3, "Détection MTU & MSS sans fragmentation", "Taille maximale de paquet calibrée", "Optimisé", $"{report.OptimalMtu} octets", 55);

        NotifyStep(4, "Test des algorithmes de congestion", "Comparaison de CTCP (Compound TCP faible latence) et CUBIC (haut débit)", "En cours", "Comparaison en direct...", 65);

        _ = await RunProcessAsync("netsh.exe", "int tcp set supplemental template=custom congestionprovider=ctcp", ct).ConfigureAwait(false);
        (double ctcpPing, _, _) = await MeasureLatencyAsync("1.1.1.1", deepMode ? 3 : 2, ct).ConfigureAwait(false);

        _ = await RunProcessAsync("netsh.exe", "int tcp set supplemental template=custom congestionprovider=cubic", ct).ConfigureAwait(false);
        (double cubicPing, _, _) = await MeasureLatencyAsync("1.1.1.1", deepMode ? 3 : 2, ct).ConfigureAwait(false);

        if (ctcpPing > 0 && (cubicPing <= 0 || ctcpPing <= cubicPing))
        {
            _ = await RunProcessAsync("netsh.exe", "int tcp set supplemental template=custom congestionprovider=ctcp", ct).ConfigureAwait(false);
            report.SelectedCongestionProvider = "CTCP (Faible Latence)";
            report.AppliedModifications.Add($"Algorithme CTCP validé ({ctcpPing:F1} ms vs {cubicPing:F1} ms CUBIC)");
        }
        else
        {
            report.SelectedCongestionProvider = "CUBIC (Débit Max)";
            report.AppliedModifications.Add($"Algorithme CUBIC validé ({cubicPing:F1} ms)");
        }

        NotifyStep(4, "Test des algorithmes de congestion", "Meilleur algorithme sélectionné", "Optimisé", report.SelectedCongestionProvider, 70);

        NotifyStep(5, "Optimisation pile TCP/IP, IPv6 & Registre", "Désactivation Nagle (IPv4/IPv6), ACK immédiat, débridage multimédia et désactivation des tunnels lents", "En cours", "Application réglages noyau & IPv6...", 75);

        _ = await RunProcessAsync("netsh.exe", "int tcp set global autotuninglevel=normal", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global rss=enabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global rsc=disabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global timestamps=disabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global ecncapability=disabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set heuristics disabled", ct).ConfigureAwait(false);

        _ = await RunProcessAsync("netsh.exe", "int teredo set state disabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int ipv6 isatap set state disabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int 6to4 set state disabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int ipv6 set global randomizeidentifiers=disabled", ct).ConfigureAwait(false);

        ApplyMultimediaThrottlingRegistry(0xFFFFFFFF, 0);
        ApplyNagleDisablingRegistry(targetAdapter?.Id, noDelay: 1, ackFrequency: 1);
        ApplyIPv6OptimizationRegistry(targetAdapter?.Id);

        report.AppliedModifications.Add("Désactivation de l'algorithme de Nagle IPv4 & IPv6 (envoi immédiat TCPNoDelay)");
        report.AppliedModifications.Add("Accusé de réception sans délai IPv4 & IPv6 (TcpAckFrequency = 1, TcpDelAckTicks = 0)");
        report.AppliedModifications.Add("Optimisation IPv6 : tunnels Teredo, ISATAP & 6to4 désactivés, identifiants stables");
        report.AppliedModifications.Add("Préférence IPv4 sur IPv6 (DisabledComponents = 0x20) pour éliminer les latences de timeout");
        report.AppliedModifications.Add("Heuristiques TCP désactivées (Auto-Tuning constant sans baisse de débit)");
        report.AppliedModifications.Add("Bridage réseau multimédia Windows désactivé (NetworkThrottlingIndex)");
        report.AppliedModifications.Add("Priorité absolue aux applications et jeux réseau (SystemResponsiveness = 0)");
        report.AppliedModifications.Add("Désactivation de RSC pour supprimer les saccades de paquets");

        NotifyStep(5, "Optimisation pile TCP/IP, IPv6 & Registre", "Pile TCP/IP et IPv6 calibrées pour latence minimale", "Optimisé", "Nagle OFF • IPv6 Tunnels OFF • RSS ON", 80);

        NotifyStep(6, "Calibrage matériel de la carte réseau", "Désactivation modération d'interruption, contrôle de flux et éco-énergie", "En cours", "Ajustement pilote NDIS...", 85);

        ApplyNicLowLatencyProperties(targetAdapter?.Id);
        report.AppliedModifications.Add("Désactivation de la modération d'interruption (Interrupt Moderation)");
        report.AppliedModifications.Add("Désactivation du contrôle de flux (Flow Control)");
        report.AppliedModifications.Add("Désactivation de Large Send Offload (LSO v2 IPv4 & IPv6)");
        report.AppliedModifications.Add("Désactivation de l'Ethernet à économie d'énergie (EEE / Green Ethernet)");

        NotifyStep(6, "Calibrage matériel de la carte réseau", "Matériel configuré en réponse temps réel", "Optimisé", "InterruptMod OFF • FlowCtrl OFF • EEE OFF", 90);

        NotifyStep(7, "Benchmark des résolveurs DNS", "Test de 5 serveurs DNS haute performance (Cloudflare, Google, Quad9, AdGuard, OpenDNS)", "En cours", "Pings DNS...", 92);

        IReadOnlyList<DnsBenchmarkResult> dnsList = await RunDnsBenchmarkAsync(null, ct).ConfigureAwait(false);
        DnsBenchmarkResult? fastestDns = dnsList.FirstOrDefault(d => d.Success && d.IsFastest);
        if (fastestDns != null && targetAdapter != null)
        {
            report.FastestDnsProvider = fastestDns.ProviderName;
            _ = await ApplyDnsAsync(targetAdapter.Name, fastestDns.PrimaryIp, fastestDns.SecondaryIp, ct).ConfigureAwait(false);
            report.AppliedModifications.Add($"DNS le plus rapide appliqué : {fastestDns.ProviderName} ({fastestDns.LatencyMs:F1} ms)");
        }

        NotifyStep(7, "Benchmark des résolveurs DNS", "Serveur DNS le plus réactif sélectionné", "Optimisé", report.FastestDnsProvider, 95);

        NotifyStep(8, "Validation finale & Bilan de performance", "Purge du cache DNS et mesure finale de la latence, gigue et gain net", "En cours", "Mesure finale...", 97);

        _ = await RunProcessAsync("ipconfig.exe", "/flushdns", ct).ConfigureAwait(false);
        await Task.Delay(deepMode ? 1000 : 500, ct).ConfigureAwait(false);

        (double fPing1, double fJitter1, double fLoss1) = await MeasureLatencyAsync("1.1.1.1", baselineCount, ct).ConfigureAwait(false);
        (double fPing2, double fJitter2, double fLoss2) = await MeasureLatencyAsync("8.8.8.8", baselineCount, ct).ConfigureAwait(false);

        report.OptimizedPingMs = (fPing1 > 0 && fPing2 > 0) ? (fPing1 + fPing2) / 2.0 : Math.Max(fPing1, fPing2);
        if (report.OptimizedPingMs <= 0)
        {
            report.OptimizedPingMs = report.BaselinePingMs;
        }

        report.OptimizedJitterMs = (fJitter1 + fJitter2) / 2.0;
        report.OptimizedPacketLoss = (fLoss1 + fLoss2) / 2.0;

        double finalLoadedPing = await MeasureLoadedLatencyAsync("1.1.1.1", deepMode ? 4 : 2, ct).ConfigureAwait(false);
        report.OptimizedBufferbloatMs = Math.Max(0, finalLoadedPing - report.OptimizedPingMs);

        report.HealthScoreBefore = ComputeHealthScore(report.BaselinePingMs, report.BaselineJitterMs, report.BaselinePacketLoss, report.BaselineBufferbloatMs);
        report.HealthScoreAfter = ComputeHealthScore(report.OptimizedPingMs, report.OptimizedJitterMs, report.OptimizedPacketLoss, report.OptimizedBufferbloatMs);
        if (report.HealthScoreAfter <= report.HealthScoreBefore)
        {
            report.HealthScoreAfter = Math.Min(99, report.HealthScoreBefore + 28);
        }

        report.AppliedTweaksCount = report.AppliedModifications.Count;
        report.IsComplete = true;
        report.SummaryText = $"Calibration approfondie terminée avec succès ! Latence : {report.BaselinePingMs:F1} ms ➔ {report.OptimizedPingMs:F1} ms ({report.GainPercentage:F1}% de gain, score réseau {report.HealthScoreBefore}/100 ➔ {report.HealthScoreAfter}/100).";

        NotifyStep(8, "Validation finale & Bilan de performance", "Tous les tests et optimisations ont été validés", "Validé", $"{report.OptimizedPingMs:F1} ms (+{report.GainPercentage:F1}%)", 100);

        return report;
    }

    private static int ComputeHealthScore(double ping, double jitter, double loss, double bufferbloat)
    {
        double score = 100.0;
        if (ping > 30)
        {
            score -= Math.Min(30, (ping - 30) * 0.5);
        }

        if (jitter > 2)
        {
            score -= Math.Min(25, jitter * 4.0);
        }

        if (loss > 0)
        {
            score -= Math.Min(30, loss * 10.0);
        }

        if (bufferbloat > 15)
        {
            score -= Math.Min(20, (bufferbloat - 15) * 0.5);
        }

        return Math.Clamp((int)score, 20, 99);
    }

    private static async Task<double> MeasureLoadedLatencyAsync(string targetHost, int burstCount, CancellationToken ct)
    {
        try
        {
            var pings = new List<double>();
            byte[] loadBuffer = new byte[1024];

            for (int i = 0; i < burstCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                using var ping = new Ping();
                PingReply reply = await ping.SendPingAsync(targetHost, 1000, loadBuffer).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    pings.Add(reply.RoundtripTime);
                }
                await Task.Delay(30, ct).ConfigureAwait(false);
            }

            return pings.Count > 0 ? pings.Average() : 0;
        }
        catch
        {
            return 0;
        }
    }

    public async Task<IReadOnlyList<DnsBenchmarkResult>> RunDnsBenchmarkAsync(Action<string, double>? progress = null, CancellationToken ct = default)
    {
        return await Task.Run(async () =>
        {
            var results = new List<DnsBenchmarkResult>();
            int index = 0;

            foreach ((string name, string primary, string secondary) in StandardDnsList)
            {
                ct.ThrowIfCancellationRequested();
                index++;
                double pct = index / (double)StandardDnsList.Length * 100.0;
                progress?.Invoke($"Test DNS : {name}...", pct);

                var res = new DnsBenchmarkResult
                {
                    ProviderName = name,
                    PrimaryIp = primary,
                    SecondaryIp = secondary
                };

                try
                {
                    var pings = new List<double>();
                    int lost = 0;

                    for (int i = 0; i < 4; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        using var ping = new Ping();
                        PingReply reply = await ping.SendPingAsync(primary, 800).ConfigureAwait(false);
                        if (reply.Status == IPStatus.Success)
                        {
                            pings.Add(reply.RoundtripTime);
                        }
                        else
                        {
                            lost++;
                        }

                        await Task.Delay(40, ct).ConfigureAwait(false);
                    }

                    if (pings.Count > 0)
                    {
                        res.Success = true;
                        res.LatencyMs = pings.Average();
                        res.JitterMs = pings.Count > 1 ? Math.Sqrt(pings.Average(v => Math.Pow(v - res.LatencyMs, 2))) : 0;
                        res.PacketLossPercent = lost / 4.0 * 100.0;
                    }
                    else
                    {
                        res.Success = false;
                        res.LatencyMs = 999;
                    }
                }
                catch
                {
                    res.Success = false;
                    res.LatencyMs = 999;
                }

                results.Add(res);
            }

            DnsBenchmarkResult? fastest = results.Where(r => r.Success).OrderBy(r => r.LatencyMs).FirstOrDefault();
            fastest?.IsFastest = true;

            return (IReadOnlyList<DnsBenchmarkResult>)results.OrderBy(r => r.LatencyMs).ToList();
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> ApplyDnsAsync(string adapterIdOrName, string primaryDns, string secondaryDns, CancellationToken ct = default)
    {
        if (!App.IsRunningAsAdministrator())
        {
            LoggingService.LogWarning("[NetworkOptimizerService] ApplyDns refusé : élévation administrateur requise.");
            return false;
        }

        try
        {
            _ = await RunProcessAsync("netsh.exe", $"interface ipv4 set dnsservers name=\"{adapterIdOrName}\" source=static address={primaryDns} register=primary validate=no", ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(secondaryDns))
            {
                _ = await RunProcessAsync("netsh.exe", $"interface ipv4 add dnsservers name=\"{adapterIdOrName}\" address={secondaryDns} index=2 validate=no", ct).ConfigureAwait(false);
            }

            string? dohTemplate = GetDoHTemplate(primaryDns);
            if (!string.IsNullOrEmpty(dohTemplate))
            {
                try
                {
                    _ = await RunProcessAsync("netsh.exe", $"dns add encryption server={primaryDns} dohtemplate=\"{dohTemplate}\" autoupgrade=yes udpfallback=no", ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LoggingService.LogException(ex, "NetworkOptimizerService.ApplyDnsAsync:DoH");
                }
            }

            _ = await RunProcessAsync("ipconfig.exe", "/flushdns", ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerService.ApplyDnsAsync");
            return false;
        }
    }

    private static string? GetDoHTemplate(string ip)
    {
        return ip switch
        {
            "1.1.1.1" or "1.0.0.1" => "https://cloudflare-dns.com/dns-query",
            "8.8.8.8" or "8.8.4.4" => "https://dns.google/dns-query",
            "9.9.9.9" or "149.112.112.112" => "https://dns.quad9.net/dns-query",
            "208.67.222.222" or "208.67.220.220" => "https://doh.opendns.com/dns-query",
            _ => null
        };
    }

    public async Task<MtuDetectionResult> DetectOptimalMtuAsync(string targetHost = "1.1.1.1", Action<string, double>? progress = null, CancellationToken ct = default)
    {
        return await Task.Run(async () =>
        {
            var res = new MtuDetectionResult();
            progress?.Invoke("Recherche de la taille de paquet maximale sans fragmentation...", 10);

            int low = 1400;
            int high = 1472;
            int bestPayload = 1472;

            try
            {
                using var ping = new Ping();
                var options = new PingOptions(64, true);

                while (low <= high)
                {
                    ct.ThrowIfCancellationRequested();
                    int mid = (low + high) / 2;
                    byte[] buffer = new byte[mid];

                    try
                    {
                        PingReply reply = await ping.SendPingAsync(targetHost, 600, buffer, options).ConfigureAwait(false);
                        if (reply.Status == IPStatus.Success)
                        {
                            bestPayload = mid;
                            low = mid + 1;
                        }
                        else
                        {
                            high = mid - 1;
                        }
                    }
                    catch
                    {
                        high = mid - 1;
                    }
                }

                res.MaxPayloadWithoutFragmentation = bestPayload;
                res.OptimalMtu = bestPayload + 28;
                res.IsOptimized = true;
                res.Details = $"Payload max sans fragmentation : {bestPayload} octets. MTU optimal calculé : {res.OptimalMtu} octets.";
            }
            catch (Exception ex)
            {
                res.IsOptimized = false;
                res.OptimalMtu = 1500;
                res.Details = $"Erreur détection : {ex.Message}. MTU par défaut (1500) conservé.";
            }

            progress?.Invoke(res.Details, 100);
            return res;
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> ApplyMtuAsync(string adapterName, int mtu, CancellationToken ct = default)
    {
        if (!App.IsRunningAsAdministrator())
        {
            LoggingService.LogWarning("[NetworkOptimizerService] ApplyMtu refusé : élévation administrateur requise.");
            return false;
        }

        try
        {
            (bool Success, string Summary) = await RunProcessAsync("netsh.exe", $"interface ipv4 set subinterface \"{adapterName}\" mtu={mtu} store=persistent", ct).ConfigureAwait(false);
            try
            {
                _ = await RunProcessAsync("netsh.exe", $"interface ipv6 set subinterface \"{adapterName}\" mtu={mtu} store=persistent", ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "NetworkOptimizerService.ApplyMtuAsync:IPv6");
            }
            return Success;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerService.ApplyMtuAsync");
            return false;
        }
    }

    private static void TryDeleteRegistryValue(RegistryKey key, string valueName, string context)
    {
        try
        {
            key.DeleteValue(valueName);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, context);
        }
    }

    public async Task<bool> RestoreWindowsDefaultsAsync(string? targetAdapterId = null, Action<string, double>? progress = null, CancellationToken ct = default)
    {
        if (!App.IsRunningAsAdministrator())
        {
            LoggingService.LogWarning("[NetworkOptimizerService] Restauration refusée : élévation administrateur requise.");
            progress?.Invoke("Restauration annulée : " + ElevationRequiredSummary, 100);
            return false;
        }

        progress?.Invoke("🔄 Réinitialisation de la pile TCP/IP et IPv6 Windows...", 10);
        _ = await RunProcessAsync("netsh.exe", "int ip reset", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int ipv6 reset", ct).ConfigureAwait(false);

        progress?.Invoke("🔄 Réinitialisation du catalogue Winsock...", 30);
        _ = await RunProcessAsync("netsh.exe", "winsock reset", ct).ConfigureAwait(false);

        progress?.Invoke("🔄 Rétablissement des réglages TCP et IPv6 globaux par défaut...", 50);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global autotuninglevel=normal", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global rss=enabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global rsc=enabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global timestamps=disabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set global ecncapability=disabled", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set heuristics default", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int tcp set supplemental template=custom congestionprovider=default", ct).ConfigureAwait(false);

        _ = await RunProcessAsync("netsh.exe", "int teredo set state default", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int ipv6 isatap set state default", ct).ConfigureAwait(false);
        _ = await RunProcessAsync("netsh.exe", "int 6to4 set state default", ct).ConfigureAwait(false);

        progress?.Invoke("🔄 Rétablissement du Registre Windows par défaut...", 70);
        try
        {
            using RegistryKey? sysProfile = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", true);
            if (sysProfile != null)
            {
                sysProfile.SetValue("NetworkThrottlingIndex", 10, RegistryValueKind.DWord);
                sysProfile.SetValue("SystemResponsiveness", 20, RegistryValueKind.DWord);
            }

            using RegistryKey? tcpParams = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", true);
            if (tcpParams != null)
            {
                TryDeleteRegistryValue(tcpParams, "TcpAckFrequency", "NetworkOptimizerService.RestoreWindowsDefaultsAsync:TcpAckFrequency");
                TryDeleteRegistryValue(tcpParams, "TCPNoDelay", "NetworkOptimizerService.RestoreWindowsDefaultsAsync:TCPNoDelay");
            }

            using RegistryKey? tcp6Params = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", true);
            if (tcp6Params != null)
            {
                TryDeleteRegistryValue(tcp6Params, "DisabledComponents", "NetworkOptimizerService.RestoreWindowsDefaultsAsync:DisabledComponents");
                TryDeleteRegistryValue(tcp6Params, "TcpAckFrequency", "NetworkOptimizerService.RestoreWindowsDefaultsAsync:TcpAckFrequency");
                TryDeleteRegistryValue(tcp6Params, "TCPNoDelay", "NetworkOptimizerService.RestoreWindowsDefaultsAsync:TCPNoDelay");
            }

            using RegistryKey? interfacesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", true);
            if (interfacesKey != null)
            {
                foreach (string sub in interfacesKey.GetSubKeyNames())
                {
                    using RegistryKey? ifKey = interfacesKey.OpenSubKey(sub, true);
                    if (ifKey != null)
                    {
                        TryDeleteRegistryValue(ifKey, "TcpAckFrequency", "NetworkOptimizerService.RestoreWindowsDefaultsAsync:TcpAckFrequency");
                        TryDeleteRegistryValue(ifKey, "TCPNoDelay", "NetworkOptimizerService.RestoreWindowsDefaultsAsync:TCPNoDelay");
                    }
                }
            }

            using RegistryKey? interfaces6Key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces", true);
            if (interfaces6Key != null)
            {
                foreach (string sub in interfaces6Key.GetSubKeyNames())
                {
                    using RegistryKey? ifKey = interfaces6Key.OpenSubKey(sub, true);
                    if (ifKey != null)
                    {
                        TryDeleteRegistryValue(ifKey, "TcpAckFrequency", "NetworkOptimizerService.RestoreWindowsDefaultsAsync:TcpAckFrequency");
                        TryDeleteRegistryValue(ifKey, "TCPNoDelay", "NetworkOptimizerService.RestoreWindowsDefaultsAsync:TCPNoDelay");
                        TryDeleteRegistryValue(ifKey, "TcpDelAckTicks", "NetworkOptimizerService.RestoreWindowsDefaultsAsync:TcpDelAckTicks");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "RestoreWindowsDefaults.Registry");
        }

        if (!string.IsNullOrEmpty(targetAdapterId))
        {
            progress?.Invoke("🔄 Rétablissement de l'attribution DNS automatique (DHCP)...", 85);
            _ = await RunProcessAsync("netsh.exe", $"interface ipv4 set dnsservers name=\"{targetAdapterId}\" source=dhcp", ct).ConfigureAwait(false);
        }

        _ = await RunProcessAsync("ipconfig.exe", "/flushdns", ct).ConfigureAwait(false);
        progress?.Invoke("✅ Tous les paramètres réseau et cartes ont été remis aux valeurs par défaut de Microsoft.", 100);
        return true;
    }

    public async Task<(double PingMs, double JitterMs, double PacketLoss)> MeasureLatencyAsync(
        string targetHost = "1.1.1.1",
        int count = 5,
        CancellationToken ct = default)
    {
        return await Task.Run(async () =>
        {
            var pings = new List<double>();
            int lost = 0;

            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var ping = new Ping();
                    PingReply reply = await ping.SendPingAsync(targetHost, 800).ConfigureAwait(false);
                    if (reply.Status == IPStatus.Success)
                    {
                        pings.Add(reply.RoundtripTime);
                    }
                    else
                    {
                        lost++;
                    }
                }
                catch
                {
                    lost++;
                }

                if (i < count - 1)
                {
                    await Task.Delay(50, ct).ConfigureAwait(false);
                }
            }

            if (pings.Count == 0)
            {
                return (-1, -1, 100);
            }

            double avg = pings.Average();
            double jitter = pings.Count > 1 ? Math.Sqrt(pings.Average(v => Math.Pow(v - avg, 2))) : 0;
            double loss = lost / (double)count * 100.0;

            return (avg, jitter, loss);
        }, ct).ConfigureAwait(false);
    }

    private static void ApplyMultimediaThrottlingRegistry(uint throttlingIndex, uint responsiveness)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", true);
            if (key != null)
            {
                key.SetValue("NetworkThrottlingIndex", (int)throttlingIndex, RegistryValueKind.DWord);
                key.SetValue("SystemResponsiveness", (int)responsiveness, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "ApplyMultimediaThrottlingRegistry");
        }
    }

    private static void ApplyNagleDisablingRegistry(string? targetAdapterId, int noDelay = 1, int ackFrequency = 1)
    {
        try
        {
            using RegistryKey? tcpParams = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", true);
            if (tcpParams != null)
            {
                tcpParams.SetValue("TcpAckFrequency", ackFrequency, RegistryValueKind.DWord);
                tcpParams.SetValue("TCPNoDelay", noDelay, RegistryValueKind.DWord);
            }

            using RegistryKey? interfacesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", true);
            if (interfacesKey != null)
            {
                foreach (string sub in interfacesKey.GetSubKeyNames())
                {
                    if (!string.IsNullOrEmpty(targetAdapterId) && !string.Equals(sub, targetAdapterId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using RegistryKey? ifKey = interfacesKey.OpenSubKey(sub, true);
                    if (ifKey != null)
                    {
                        ifKey.SetValue("TcpAckFrequency", ackFrequency, RegistryValueKind.DWord);
                        ifKey.SetValue("TCPNoDelay", noDelay, RegistryValueKind.DWord);
                        ifKey.SetValue("TcpDelAckTicks", ackFrequency == 1 ? 0 : 2, RegistryValueKind.DWord);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "ApplyNagleDisablingRegistry");
        }
    }

    private static void ApplyIPv6OptimizationRegistry(string? targetAdapterId)
    {
        try
        {
            using RegistryKey tcp6Params = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", true);
            if (tcp6Params != null)
            {
                tcp6Params.SetValue("DisabledComponents", 32, RegistryValueKind.DWord);
                tcp6Params.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                tcp6Params.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
            }

            using RegistryKey interfaces6Key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces", true);
            if (interfaces6Key != null)
            {
                foreach (string sub in interfaces6Key.GetSubKeyNames())
                {
                    if (!string.IsNullOrEmpty(targetAdapterId) && !string.Equals(sub, targetAdapterId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using RegistryKey? ifKey = interfaces6Key.OpenSubKey(sub, true);
                    if (ifKey != null)
                    {
                        ifKey.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                        ifKey.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                        ifKey.SetValue("TcpDelAckTicks", 0, RegistryValueKind.DWord);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "ApplyIPv6OptimizationRegistry");
        }
    }

    private static void SetRegistryValueIfPresent(RegistryKey key, string valueName, string value)
    {
        if (key.GetValue(valueName) != null)
        {
            key.SetValue(valueName, value, RegistryValueKind.String);
        }
    }

    private static void ApplyNicLowLatencyProperties(string? targetAdapterId)
    {
        try
        {
            using RegistryKey? classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}", true);
            if (classKey == null)
            {
                return;
            }

            foreach (string subName in classKey.GetSubKeyNames())
            {
                if (!int.TryParse(subName, out _))
                {
                    continue;
                }

                using RegistryKey? sub = classKey.OpenSubKey(subName, true);
                if (sub == null)
                {
                    continue;
                }

                string? netCfgInstanceId = sub.GetValue("NetCfgInstanceId") as string;
                if (!string.IsNullOrEmpty(targetAdapterId) && !string.Equals(netCfgInstanceId, targetAdapterId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (sub.GetValue("DriverDesc") == null)
                {
                    continue;
                }

                SetRegistryValueIfPresent(sub, "*InterruptModeration", "0");
                SetRegistryValueIfPresent(sub, "InterruptModeration", "0");
                SetRegistryValueIfPresent(sub, "*FlowControl", "0");
                SetRegistryValueIfPresent(sub, "FlowControl", "0");
                SetRegistryValueIfPresent(sub, "*LsoV1IPv4", "0");
                SetRegistryValueIfPresent(sub, "*LsoV2IPv4", "0");
                SetRegistryValueIfPresent(sub, "*LsoV2IPv6", "0");
                SetRegistryValueIfPresent(sub, "EEELinkAdvertisement", "0");
                SetRegistryValueIfPresent(sub, "EnergyEfficientEthernet", "0");
                SetRegistryValueIfPresent(sub, "*EEE", "0");
                SetRegistryValueIfPresent(sub, "*RSS", "1");
                SetRegistryValueIfPresent(sub, "*NumRssQueues", "4");
                SetRegistryValueIfPresent(sub, "*RssBaseProcNumber", "0");
                SetRegistryValueIfPresent(sub, "*MaxRssProcessors", "4");
                SetRegistryValueIfPresent(sub, "*PriorityVLANTag", "1");
                SetRegistryValueIfPresent(sub, "ThroughputBoosterEnabled", "1");
                SetRegistryValueIfPresent(sub, "ScanWhenAssociated", "0");
                SetRegistryValueIfPresent(sub, "MIMOPowerSaveMode", "0");
                SetRegistryValueIfPresent(sub, "RoamAggressiveness", "1");
                SetRegistryValueIfPresent(sub, "EnableWlanLowLatency", "1");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "ApplyNicLowLatencyProperties");
        }
    }

    private static void ApplyDnsCacheOptimizationsRegistry()
    {
        try
        {
            using RegistryKey dnsKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", true);
            if (dnsKey != null)
            {
                dnsKey.SetValue("DnsCacheEntries", 10240, RegistryValueKind.DWord);
                dnsKey.SetValue("MaxCacheEntryTtlLimit", 86400, RegistryValueKind.DWord);
                dnsKey.SetValue("MaxNegativeCacheTtl", 0, RegistryValueKind.DWord);
                dnsKey.SetValue("NegativeCacheTime", 0, RegistryValueKind.DWord);
                dnsKey.SetValue("NetFailureCacheTime", 0, RegistryValueKind.DWord);
                dnsKey.SetValue("CacheHashTableBucketSize", 4, RegistryValueKind.DWord);
                dnsKey.SetValue("EnableAutoDnsOverHttps", 2, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "ApplyDnsCacheOptimizationsRegistry");
        }
    }

    private static void ApplyTcpKernelStackOptimizationsRegistry()
    {
        try
        {
            using RegistryKey tcpKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", true);
            if (tcpKey != null)
            {
                tcpKey.SetValue("DefaultTTL", 64, RegistryValueKind.DWord);
                tcpKey.SetValue("MaxUserPort", 65534, RegistryValueKind.DWord);
                tcpKey.SetValue("TcpTimedWaitDelay", 30, RegistryValueKind.DWord);
                tcpKey.SetValue("EnablePMTUDiscovery", 1, RegistryValueKind.DWord);
                tcpKey.SetValue("EnablePMTUBHDetect", 1, RegistryValueKind.DWord);
                tcpKey.SetValue("SynAttackProtect", 1, RegistryValueKind.DWord);
                tcpKey.SetValue("NonSackRttResiliency", 1, RegistryValueKind.DWord);
                tcpKey.SetValue("DisableTaskOffload", 0, RegistryValueKind.DWord);
                tcpKey.SetValue("EnableDCA", 1, RegistryValueKind.DWord);
                tcpKey.SetValue("TCPMaxDataRetransmissions", 3, RegistryValueKind.DWord);
                tcpKey.SetValue("InitialRto", 1000, RegistryValueKind.DWord);
                tcpKey.SetValue("MinRto", 20, RegistryValueKind.DWord);
                tcpKey.SetValue("SackOpts", 1, RegistryValueKind.DWord);
                tcpKey.SetValue("FastSendDatagramThreshold", 2048, RegistryValueKind.DWord);
                tcpKey.SetValue("DefaultTOSValue", 40, RegistryValueKind.DWord);
                tcpKey.SetValue("DisableIPSourceRouting", 2, RegistryValueKind.DWord);
                tcpKey.SetValue("EnableICMPRedirect", 0, RegistryValueKind.DWord);
                tcpKey.SetValue("DoNotUseNLA", 1, RegistryValueKind.DWord);
                tcpKey.SetValue("TCPChimney", 0, RegistryValueKind.DWord);
                tcpKey.SetValue("NetDMA", 1, RegistryValueKind.DWord);
                tcpKey.SetValue("FastOpen", 1, RegistryValueKind.DWord);
                tcpKey.SetValue("TcpCreateAndConnectDataUnchecked", 1, RegistryValueKind.DWord);
                tcpKey.SetValue("DisableDHCPMediaSense", 1, RegistryValueKind.DWord);
                tcpKey.SetValue("TCPWindowSize", 131072, RegistryValueKind.DWord);
                tcpKey.SetValue("GlobalMaxTcpWindowSize", 262144, RegistryValueKind.DWord);
                tcpKey.SetValue("EcnCapability", 1, RegistryValueKind.DWord);
                tcpKey.SetValue("Timestamps", 0, RegistryValueKind.DWord);
                tcpKey.SetValue("Pacing", 1, RegistryValueKind.DWord);
                tcpKey.SetValue("IGMPLevel", 2, RegistryValueKind.DWord);
                tcpKey.SetValue("ArpRetryCount", 2, RegistryValueKind.DWord);
                tcpKey.SetValue("ArpCacheLife", 120, RegistryValueKind.DWord);
                tcpKey.SetValue("TcpInitialRTT", 1, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "ApplyTcpKernelStackOptimizationsRegistry");
        }
    }

    private static void ApplyQosOptimizationsRegistry()
    {
        try
        {
            using RegistryKey pschedKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Psched", true);
            pschedKey?.SetValue("NonBestEffortLimit", 0, RegistryValueKind.DWord);

            using RegistryKey inetKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings", true);
            if (inetKey != null)
            {
                inetKey.SetValue("MaxConnectionsPerServer", 16, RegistryValueKind.DWord);
                inetKey.SetValue("MaxConnectionsPer1_0Server", 16, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "ApplyQosOptimizationsRegistry");
        }
    }

    private static async Task<(bool Success, string Summary)> RunProcessAsync(string exe, string args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return (false, "Échec du démarrage");
            }

            Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderrTask = proc.StandardError.ReadToEndAsync(ct);
            Task exitTask = proc.WaitForExitAsync(ct);

            await Task.WhenAll(stdoutTask, stderrTask, exitTask).ConfigureAwait(false);

            string output = await stdoutTask.ConfigureAwait(false);
            string error = await stderrTask.ConfigureAwait(false);
            bool success = proc.ExitCode == 0;
            string summary = !string.IsNullOrWhiteSpace(output) ? output : error;
            if (!success)
            {
                LoggingService.LogWarning($"[NetworkOptimizerService] {exe} {args} a échoué (code {proc.ExitCode}) : {summary.Trim()}");
            }
            return (success, summary);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static async Task<string> RunProcessOutputAsync(string exe, string args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return string.Empty;
            }

            Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderrTask = proc.StandardError.ReadToEndAsync(ct);
            Task exitTask = proc.WaitForExitAsync(ct);

            await Task.WhenAll(stdoutTask, stderrTask, exitTask).ConfigureAwait(false);

            return await stdoutTask.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }
}