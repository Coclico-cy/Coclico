using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Coclico.Models.Network;
using Microsoft.Win32;

namespace Coclico.Services.Network;

[SupportedOSPlatform("windows")]
public sealed class NetworkDiscoveryService : INetworkDiscoveryService
{
    private const string NetworkClassKeyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    private static readonly string[] ForbiddenVirtualKeywords =
    [
        "virtual", "filter", "scheduler", "teredo", "isatap", "6to4",
        "bluetooth", "miniport", "kernel debug", "wfp", "lightweight",
        "direct", "pseudo", "tunneling", "vpn", "wireguard", "tailscale",
        "tap-windows", "npcap", "pcap", "vmware", "hyper-v", "virtualbox",
        "débogueur", "connexion au réseau local*"
    ];

    private static readonly Regex SignalPercentRegex = new(@"(\d+)\s*%", RegexOptions.Compiled);

    public async Task<IReadOnlyList<NetworkAdapterHardwareDetails>> DiscoverPhysicalAdaptersAsync(CancellationToken ct = default)
    {
        return await Task.Run(async () =>
        {
            var results = new List<NetworkAdapterHardwareDetails>();
            Dictionary<string, (string DriverDesc, string DriverVersion, string DriverDate, string DriverProvider, string MatchingDeviceId, string PnpId)> physicalMeta = GetPhysicalAdapterRegistryMetadata();
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

                if (physicalMeta.Count > 0 && !physicalMeta.ContainsKey(ni.Id))
                {
                    continue;
                }

                _ = physicalMeta.TryGetValue(ni.Id, out (string DriverDesc, string DriverVersion, string DriverDate, string DriverProvider, string MatchingDeviceId, string PnpId) meta);

                NetworkAdapterType type = ni.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => NetworkAdapterType.Wireless80211,
                    NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => NetworkAdapterType.Ethernet,
                    _ => NetworkAdapterType.Other
                };

                IPInterfaceProperties ipProps = ni.GetIPProperties();
                string ipv4 = ipProps.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? string.Empty;

                string gateway = ipProps.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? string.Empty;

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
                    LoggingService.LogException(ex, "NetworkDiscoveryService.DiscoverPhysicalAdaptersAsync:Mtu");
                }

                string mac = ni.GetPhysicalAddress().ToString();
                if (mac.Length == 12)
                {
                    mac = string.Join(":", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)));
                }

                var adapter = new NetworkAdapterHardwareDetails
                {
                    Id = ni.Id,
                    Name = ni.Name,
                    Description = ni.Description,
                    Type = type,
                    IsPhysical = true,
                    PnpInstanceId = meta.PnpId,
                    MatchingDeviceId = meta.MatchingDeviceId,
                    DriverVersion = meta.DriverVersion,
                    DriverDate = meta.DriverDate,
                    DriverProvider = meta.DriverProvider,
                    LinkSpeedBitsPerSecond = ni.Speed,
                    MacAddress = mac,
                    IPv4Address = ipv4,
                    GatewayAddress = gateway,
                    Mtu = mtu,
                    DnsServers = dns
                };

                IReadOnlyList<DynamicNetworkParameter> parameters = await DiscoverAdapterParametersAsync(ni.Id, ni.Name, ct).ConfigureAwait(false);
                adapter.DiscoveredParameters.AddRange(parameters);

                if (adapter.IsWireless)
                {
                    _ = await RefreshWifiTelemetryAsync(adapter, ct).ConfigureAwait(false);
                }

                results.Add(adapter);
            }

            return results
                .OrderByDescending(a => !string.IsNullOrEmpty(a.IPv4Address))
                .ThenBy(a => a.Type == NetworkAdapterType.Ethernet ? 0 : 1)
                .ToList();
        }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DynamicNetworkParameter>> DiscoverAdapterParametersAsync(
        string adapterIdOrGuid,
        string? adapterName = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<DynamicNetworkParameter>();
            try
            {
                using RegistryKey? classKey = Registry.LocalMachine.OpenSubKey(NetworkClassKeyPath);
                if (classKey == null)
                {
                    return list;
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

                    string? netCfgInstanceId = sub.GetValue("NetCfgInstanceId") as string;
                    if (!string.Equals(netCfgInstanceId, adapterIdOrGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using RegistryKey? ndiParamsKey = sub.OpenSubKey(@"Ndi\params");
                    if (ndiParamsKey != null)
                    {
                        foreach (string keyword in ndiParamsKey.GetSubKeyNames())
                        {
                            using RegistryKey? paramKey = ndiParamsKey.OpenSubKey(keyword);
                            if (paramKey == null)
                            {
                                continue;
                            }

                            string paramDesc = paramKey.GetValue("ParamDesc")?.ToString() ?? keyword;
                            string typeStr = paramKey.GetValue("type")?.ToString()?.ToLowerInvariant() ?? "text";
                            string defaultVal = paramKey.GetValue("default")?.ToString() ?? string.Empty;

                            var dynParam = new DynamicNetworkParameter
                            {
                                RegistryKeyword = keyword,
                                DisplayName = paramDesc,
                                DefaultValue = defaultVal,
                                CurrentValue = sub.GetValue(keyword)?.ToString() ?? defaultVal
                            };

                            if (typeStr == "enum")
                            {
                                dynParam.ValueType = DynamicParameterType.Enumeration;
                                using RegistryKey? enumKey = paramKey.OpenSubKey("enum");
                                if (enumKey != null)
                                {
                                    foreach (string optVal in enumKey.GetValueNames())
                                    {
                                        string optDisplay = enumKey.GetValue(optVal)?.ToString() ?? optVal;
                                        dynParam.ValidOptions.Add(new DynamicParameterOption
                                        {
                                            RegistryValue = optVal,
                                            DisplayName = optDisplay
                                        });

                                        if (string.Equals(optVal, dynParam.CurrentValue, StringComparison.OrdinalIgnoreCase))
                                        {
                                            dynParam.CurrentDisplayValue = optDisplay;
                                        }
                                    }
                                }
                            }
                            else if (typeStr is "int" or "dword" or "word")
                            {
                                dynParam.ValueType = DynamicParameterType.IntegerRange;
                                if (long.TryParse(paramKey.GetValue("min")?.ToString(), out long min))
                                {
                                    dynParam.MinValue = min;
                                }

                                if (long.TryParse(paramKey.GetValue("max")?.ToString(), out long max))
                                {
                                    dynParam.MaxValue = max;
                                }

                                if (long.TryParse(paramKey.GetValue("step")?.ToString(), out long step))
                                {
                                    dynParam.Step = step;
                                }

                                dynParam.CurrentDisplayValue = dynParam.CurrentValue;
                            }
                            else
                            {
                                dynParam.ValueType = DynamicParameterType.Text;
                                dynParam.CurrentDisplayValue = dynParam.CurrentValue;
                            }

                            list.Add(dynParam);
                        }
                    }
                    break;
                }

                AppendExtendedSystemParameters(adapterIdOrGuid, list);
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "NetworkDiscoveryService.DiscoverAdapterParametersAsync");
            }

            return list;
        }, ct).ConfigureAwait(false);
    }

    private static void AppendExtendedSystemParameters(string adapterId, List<DynamicNetworkParameter> list)
    {
        try
        {
            using (RegistryKey? ifKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{adapterId}"))
            {
                AddOrUpdateParam(list, "TCPNoDelay", "Algorithme de Nagle (TCPNoDelay)",
                    ifKey?.GetValue("TCPNoDelay")?.ToString() ?? "0",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1 (Désactivé - Latence immédiate)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Activé - Par défaut)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "TcpAckFrequency", "Fréquence des Acquittements (TcpAckFrequency)",
                    ifKey?.GetValue("TcpAckFrequency")?.ToString() ?? "2",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1 (ACK immédiat - Zéro attente)" },
                        new() { RegistryValue = "2", DisplayName = "2 (ACK différé par paires)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "TCPDelAckTicks", "Délai d'ACK Décalé (TCPDelAckTicks)",
                    ifKey?.GetValue("TCPDelAckTicks")?.ToString() ?? "1",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "0", DisplayName = "0 (Zéro tick - Réponse instantanée)" },
                        new() { RegistryValue = "1", DisplayName = "1 (Par défaut)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "MTU", "Taille Maximale de Trame (MTU)",
                    ifKey?.GetValue("MTU")?.ToString() ?? "1500",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1500", DisplayName = "1500 (Standard Ethernet)" },
                        new() { RegistryValue = "1492", DisplayName = "1492 (PPPoE / Fibre)" }
                    ], "MTU & Trame");
            }

            using (RegistryKey? sysProfile = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"))
            {
                AddOrUpdateParam(list, "NetworkThrottlingIndex", "Bridage Réseau Multimédia (NetworkThrottlingIndex)",
                    sysProfile?.GetValue("NetworkThrottlingIndex")?.ToString() ?? "10",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "4294967295", DisplayName = "0xFFFFFFFF (Débridé à 100%)" },
                        new() { RegistryValue = "10", DisplayName = "10 (Bridé par défaut)" }
                    ], "Windows QoS");

                AddOrUpdateParam(list, "SystemResponsiveness", "Priorité Réseau Temps Réel (SystemResponsiveness)",
                    sysProfile?.GetValue("SystemResponsiveness")?.ToString() ?? "20",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "0", DisplayName = "0 (100% Priorité Jeux/Réseau)" },
                        new() { RegistryValue = "20", DisplayName = "20 (Par défaut)" }
                    ], "Windows QoS");
            }

            using (RegistryKey? qosKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Psched"))
            {
                AddOrUpdateParam(list, "NonBestEffortLimit", "Réserve de Bande Passante QoS (NonBestEffortLimit)",
                    qosKey?.GetValue("NonBestEffortLimit")?.ToString() ?? "20",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "0", DisplayName = "0% (Aucune réserve artificielle)" },
                        new() { RegistryValue = "20", DisplayName = "20% (Par défaut Windows)" }
                    ], "Windows QoS");
            }

            using (RegistryKey? tcpKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"))
            {
                AddOrUpdateParam(list, "DefaultTTL", "Durée de Vie des Paquets (DefaultTTL)",
                    tcpKey?.GetValue("DefaultTTL")?.ToString() ?? "128",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "64", DisplayName = "64 (Optimal Faible Latence)" },
                        new() { RegistryValue = "128", DisplayName = "128 (Par défaut Windows)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "MaxUserPort", "Plage de Ports Éphémères (MaxUserPort)",
                    tcpKey?.GetValue("MaxUserPort")?.ToString() ?? "5000",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "65534", DisplayName = "65534 (Plage maximale sans goulot)" },
                        new() { RegistryValue = "5000", DisplayName = "5000 (Standard)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "TcpTimedWaitDelay", "Recyclage des Sockets Fermés (TcpTimedWaitDelay)",
                    tcpKey?.GetValue("TcpTimedWaitDelay")?.ToString() ?? "120",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "30", DisplayName = "30s (Recyclage ultra-rapide)" },
                        new() { RegistryValue = "120", DisplayName = "120s (Standard)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "EnablePMTUDiscovery", "Découverte Automatique PMTU",
                    tcpKey?.GetValue("EnablePMTUDiscovery")?.ToString() ?? "1",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1 (Activé - Prévient la fragmentation)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Désactivé)" }
                    ], "MTU & Trame");

                AddOrUpdateParam(list, "SynAttackProtect", "Protection Anti-SYN Flood (SynAttackProtect)",
                    tcpKey?.GetValue("SynAttackProtect")?.ToString() ?? "1",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1 (Activé - Protection stable)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Désactivé)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "NonSackRttResiliency", "Résilience RTT Non-SACK",
                    tcpKey?.GetValue("NonSackRttResiliency")?.ToString() ?? "0",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1 (Activé - Réduit les pertes partielles)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Désactivé)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "DisableTaskOffload", "Délestage Matériel Global (DisableTaskOffload)",
                    tcpKey?.GetValue("DisableTaskOffload")?.ToString() ?? "0",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "0", DisplayName = "0 (Délestage actif - Calcul CPU épargné)" },
                        new() { RegistryValue = "1", DisplayName = "1 (Désactivé)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "EnableDCA", "Direct Cache Access (EnableDCA)",
                    tcpKey?.GetValue("EnableDCA")?.ToString() ?? "1",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1 (Activé - Écriture directe cache L3)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Désactivé)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "TCPMaxDataRetransmissions", "Retransmissions Max Données TCP",
                    tcpKey?.GetValue("TCPMaxDataRetransmissions")?.ToString() ?? "3",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "3", DisplayName = "3 (Réactivité accrue gaming)" },
                        new() { RegistryValue = "5", DisplayName = "5 (Standard Windows)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "InitialRto", "Délai Initial RTO (InitialRto)",
                    tcpKey?.GetValue("InitialRto")?.ToString() ?? "1000",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1000", DisplayName = "1000ms (Réponse rapide)" },
                        new() { RegistryValue = "3000", DisplayName = "3000ms (Par défaut)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "MinRto", "Délai Minimal RTO (MinRto)",
                    tcpKey?.GetValue("MinRto")?.ToString() ?? "20",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "20", DisplayName = "20ms (Temps réel)" },
                        new() { RegistryValue = "300", DisplayName = "300ms (Standard)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "SackOpts", "Acquittements Sélectifs SACK",
                    tcpKey?.GetValue("SackOpts")?.ToString() ?? "1",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1 (Activé - Réduit les retransmissions)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Désactivé)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "FastSendDatagramThreshold", "Seuil d'Envoi Rapide Datagrammes",
                    tcpKey?.GetValue("FastSendDatagramThreshold")?.ToString() ?? "1024",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "2048", DisplayName = "2048 (Optimisé)" },
                        new() { RegistryValue = "1024", DisplayName = "1024 (Standard)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "DefaultTOSValue", "Priorité Type of Service (DefaultTOSValue)",
                    tcpKey?.GetValue("DefaultTOSValue")?.ToString() ?? "0",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "40", DisplayName = "40 (Priorité DSCP CS5 multimédia/jeu)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Standard non priorisé)" }
                    ], "Windows QoS");

                AddOrUpdateParam(list, "DisableIPSourceRouting", "Protection Routage Source IP",
                    tcpKey?.GetValue("DisableIPSourceRouting")?.ToString() ?? "2",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "2", DisplayName = "2 (Sécurité maximale - Rejet total)" },
                        new() { RegistryValue = "1", DisplayName = "1 (Rejet partiel)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "EnableICMPRedirect", "Redirections ICMP (EnableICMPRedirect)",
                    tcpKey?.GetValue("EnableICMPRedirect")?.ToString() ?? "0",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "0", DisplayName = "0 (Désactivé - Évite les déviations)" },
                        new() { RegistryValue = "1", DisplayName = "1 (Activé)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "DoNotUseNLA", "Contournement NLA (DoNotUseNLA)",
                    tcpKey?.GetValue("DoNotUseNLA")?.ToString() ?? "1",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1 (Activé - Zéro attente localisation)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Standard)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "EnablePMTUBHDetect", "Détection Trous Noirs MTU (EnablePMTUBHDetect)",
                    tcpKey?.GetValue("EnablePMTUBHDetect")?.ToString() ?? "1",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1 (Activé - Détecte routeurs défaillants)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Désactivé)" }
                    ], "MTU & Trame");

                AddOrUpdateParam(list, "TCPChimney", "Délestage Moteur TCP Chimney",
                    tcpKey?.GetValue("TCPChimney")?.ToString() ?? "0",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "0", DisplayName = "0 (Désactivé - Prévient les pertes de paquets)" },
                        new() { RegistryValue = "1", DisplayName = "1 (Activé)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "NetDMA", "Accès Direct Mémoire Réseau (NetDMA)",
                    tcpKey?.GetValue("NetDMA")?.ToString() ?? "1",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1 (Activé - Réduit l'usage CPU)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Désactivé)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "FastOpen", "TCP Fast Open (RFC 7413)",
                    tcpKey?.GetValue("FastOpen")?.ToString() ?? "1",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1 (Activé - Envoi immédiat dès SYN)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Désactivé)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "TcpCreateAndConnectDataUnchecked", "Fast-Path Sockets TCP",
                    tcpKey?.GetValue("TcpCreateAndConnectDataUnchecked")?.ToString() ?? "1",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1 (Activé - Connexions ultra-rapides)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Standard)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "DisableDHCPMediaSense", "Maintien Liaison IP (DisableDHCPMediaSense)",
                    tcpKey?.GetValue("DisableDHCPMediaSense")?.ToString() ?? "1",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1 (Activé - Supprime les gels réseau)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Standard)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "TCPWindowSize", "Tampon Réception TCP Initial (TCPWindowSize)",
                    tcpKey?.GetValue("TCPWindowSize")?.ToString() ?? "131072",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "131072", DisplayName = "131072 (Optimisé 128 Ko)" },
                        new() { RegistryValue = "65535", DisplayName = "65535 (Standard 64 Ko)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "GlobalMaxTcpWindowSize", "Fenêtre TCP Maximale Globale",
                    tcpKey?.GetValue("GlobalMaxTcpWindowSize")?.ToString() ?? "262144",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "262144", DisplayName = "262144 (Optimisé 256 Ko)" },
                        new() { RegistryValue = "65535", DisplayName = "65535 (Standard)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "EcnCapability", "Notification Congestion ECN (RFC 3168)",
                    tcpKey?.GetValue("EcnCapability")?.ToString() ?? "1",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1 (Activé - Réduit les pertes sous charge)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Désactivé)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "Timestamps", "Horodatage TCP (RFC 1323)",
                    tcpKey?.GetValue("Timestamps")?.ToString() ?? "0",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "0", DisplayName = "0 (Désactivé - Zéro overhead trame)" },
                        new() { RegistryValue = "1", DisplayName = "1 (Activé)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "Pacing", "Régulation d'Espacement TCP Pacing",
                    tcpKey?.GetValue("Pacing")?.ToString() ?? "1",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1 (Activé - Élimine les rafales brutales)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Désactivé)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "IGMPLevel", "Support Multicast Réseau (IGMPLevel)",
                    tcpKey?.GetValue("IGMPLevel")?.ToString() ?? "2",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "2", DisplayName = "2 (Multicast complet et fluide)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Désactivé)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "ArpRetryCount", "Tentatives de Requêtes ARP (ArpRetryCount)",
                    tcpKey?.GetValue("ArpRetryCount")?.ToString() ?? "2",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "2", DisplayName = "2 (Rapide 2 essais)" },
                        new() { RegistryValue = "3", DisplayName = "3 (Standard Windows)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "ArpCacheLife", "Durée Cache ARP en Secondes (ArpCacheLife)",
                    tcpKey?.GetValue("ArpCacheLife")?.ToString() ?? "120",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "120", DisplayName = "120s (Rafraîchissement dynamique)" },
                        new() { RegistryValue = "240", DisplayName = "240s (Standard)" }
                    ], "TCP Kernel");

                AddOrUpdateParam(list, "TcpInitialRTT", "RTT Initial d'Estimation TCP",
                    tcpKey?.GetValue("TcpInitialRTT")?.ToString() ?? "1",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1s (Accélération négociation)" },
                        new() { RegistryValue = "3", DisplayName = "3s (Standard)" }
                    ], "TCP Kernel");
            }

            using (RegistryKey? inetKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings"))
            {
                AddOrUpdateParam(list, "MaxConnectionsPerServer", "Connexions Simultanées HTTP 1.1",
                    inetKey?.GetValue("MaxConnectionsPerServer")?.ToString() ?? "16",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "16", DisplayName = "16 (Haute concurrence web/téléchargement)" },
                        new() { RegistryValue = "2", DisplayName = "2 (Par défaut HTTP/1.1)" }
                    ], "Réseau & Web");

                AddOrUpdateParam(list, "MaxConnectionsPer1_0Server", "Connexions Simultanées HTTP 1.0",
                    inetKey?.GetValue("MaxConnectionsPer1_0Server")?.ToString() ?? "16",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "16", DisplayName = "16 (Multi-sources)" },
                        new() { RegistryValue = "4", DisplayName = "4 (Par défaut HTTP/1.0)" }
                    ], "Réseau & Web");
            }

            using (RegistryKey? dnsKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters"))
            {
                AddOrUpdateParam(list, "DnsCacheEntries", "Taille du Cache DNS Résolveur (DnsCacheEntries)",
                    dnsKey?.GetValue("MaxCacheEntryTtlLimit")?.ToString() ?? "10240",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "10240", DisplayName = "10240 entrées (Large cache résolveur)" },
                        new() { RegistryValue = "1024", DisplayName = "1024 entrées (Standard)" }
                    ], "DNS & Résolution");

                AddOrUpdateParam(list, "MaxCacheEntryTtlLimit", "Durée Maximale Cache DNS (MaxCacheEntryTtlLimit)",
                    dnsKey?.GetValue("MaxCacheEntryTtlLimit")?.ToString() ?? "86400",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "86400", DisplayName = "86400s (24h de conservation)" },
                        new() { RegistryValue = "7200", DisplayName = "7200s (2h standard)" }
                    ], "DNS & Résolution");

                AddOrUpdateParam(list, "NegativeCacheTime", "Délai Cache d'Échec DNS (NegativeCacheTime)",
                    dnsKey?.GetValue("NegativeCacheTime")?.ToString() ?? "0",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "0", DisplayName = "0s (Nouvel essai immédiat sans attente)" },
                        new() { RegistryValue = "300", DisplayName = "300s (5min standard)" }
                    ], "DNS & Résolution");

                AddOrUpdateParam(list, "NetFailureCacheTime", "Cache Erreurs Réseau (NetFailureCacheTime)",
                    dnsKey?.GetValue("NetFailureCacheTime")?.ToString() ?? "0",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "0", DisplayName = "0s (Zéro mise en attente)" },
                        new() { RegistryValue = "30", DisplayName = "30s (Standard)" }
                    ], "DNS & Résolution");
            }

            using (RegistryKey? tcp6Key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters"))
            {
                AddOrUpdateParam(list, "DisabledComponents", "Priorité & Composants IPv6 (DisabledComponents)",
                    tcp6Key?.GetValue("DisabledComponents")?.ToString() ?? "0",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "32", DisplayName = "32 (0x20 - Préférer IPv4, réduit la latence DNS)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Par défaut Windows)" }
                    ], "IPv6 & Tunneling");
            }

            using (RegistryKey? if6Key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\{adapterId}"))
            {
                AddOrUpdateParam(list, "TcpNoDelayIPv6", "Algorithme de Nagle IPv6 (TCPNoDelay)",
                    if6Key?.GetValue("TCPNoDelay")?.ToString() ?? "0",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1 (Désactivé - Latence immédiate IPv6)" },
                        new() { RegistryValue = "0", DisplayName = "0 (Par défaut)" }
                    ], "IPv6 & Tunneling");

                AddOrUpdateParam(list, "TcpAckFrequencyIPv6", "Fréquence des Acquittements IPv6 (TcpAckFrequency)",
                    if6Key?.GetValue("TcpAckFrequency")?.ToString() ?? "2",
                    DynamicParameterType.Enumeration,
                    [
                        new() { RegistryValue = "1", DisplayName = "1 (ACK immédiat IPv6)" },
                        new() { RegistryValue = "2", DisplayName = "2 (Par défaut)" }
                    ], "IPv6 & Tunneling");
            }

            AddOrUpdateParam(list, "IPv6TeredoState", "Tunneling IPv6 Teredo (netsh)",
                "enabled",
                DynamicParameterType.Enumeration,
                [
                    new() { RegistryValue = "disabled", DisplayName = "disabled (Désactivé - Supprime micro-saccades)" },
                    new() { RegistryValue = "default", DisplayName = "default (Par défaut Windows)" }
                ], "IPv6 & Tunneling");

            AddOrUpdateParam(list, "IPv6IsatapState", "Tunneling IPv6 ISATAP (netsh)",
                "enabled",
                DynamicParameterType.Enumeration,
                [
                    new() { RegistryValue = "disabled", DisplayName = "disabled (Désactivé - Évite routage parasite)" },
                    new() { RegistryValue = "default", DisplayName = "default (Par défaut Windows)" }
                ], "IPv6 & Tunneling");

            AddOrUpdateParam(list, "IPv66to4State", "Tunneling IPv6 6to4 (netsh)",
                "enabled",
                DynamicParameterType.Enumeration,
                [
                    new() { RegistryValue = "disabled", DisplayName = "disabled (Désactivé - Obsolète)" },
                    new() { RegistryValue = "default", DisplayName = "default (Par défaut Windows)" }
                ], "IPv6 & Tunneling");

            AddOrUpdateParam(list, "TcpHeuristics", "Heuristiques de Fenêtre TCP (netsh)",
                "enabled",
                DynamicParameterType.Enumeration,
                [
                    new() { RegistryValue = "disabled", DisplayName = "disabled (Désactivé - Maintient auto-tuning)" },
                    new() { RegistryValue = "enabled", DisplayName = "enabled (Par défaut Windows)" }
                ], "TCP Kernel");

            AddOrUpdateParam(list, "CongestionProvider", "Contrôle de Congestion TCP (netsh)",
                "cubic",
                DynamicParameterType.Enumeration,
                [
                    new() { RegistryValue = "ctcp", DisplayName = "ctcp (Compound TCP - Latence minimale gaming)" },
                    new() { RegistryValue = "bbr", DisplayName = "bbr (Bottleneck Bandwidth and RTT - Très haut débit)" },
                    new() { RegistryValue = "cubic", DisplayName = "cubic (Standard moderne)" },
                    new() { RegistryValue = "newreno", DisplayName = "newreno (Standard historique)" }
                ], "TCP Kernel");

            AddOrUpdateParam(list, "AutoTuningLevel", "Niveau Auto-Tuning Fenêtre TCP (netsh)",
                "normal",
                DynamicParameterType.Enumeration,
                [
                    new() { RegistryValue = "normal", DisplayName = "normal (Équilibré - Recommandé)" },
                    new() { RegistryValue = "experimental", DisplayName = "experimental (Débit maximal fibre)" },
                    new() { RegistryValue = "highlyrestricted", DisplayName = "highlyrestricted (Très conservateur)" },
                    new() { RegistryValue = "disabled", DisplayName = "disabled (Fenêtre fixe 64 Ko)" }
                ], "TCP Kernel");

            AddOrUpdateParam(list, "TCPRss", "Receive Side Scaling Global (netsh)",
                "enabled",
                DynamicParameterType.Enumeration,
                [
                    new() { RegistryValue = "enabled", DisplayName = "enabled (Activé - Répartition multi-cœurs)" },
                    new() { RegistryValue = "disabled", DisplayName = "disabled (Désactivé - Monothread)" }
                ], "TCP Kernel");

            AddOrUpdateParam(list, "TCPRsc", "Receive Segment Coalescing Global (netsh)",
                "disabled",
                DynamicParameterType.Enumeration,
                [
                    new() { RegistryValue = "disabled", DisplayName = "disabled (Désactivé - Zéro gigue gaming)" },
                    new() { RegistryValue = "enabled", DisplayName = "enabled (Activé - Regroupement haut débit)" }
                ], "TCP Kernel");

            AddOrUpdateParam(list, "TCPTimestamps", "Horodatages RFC 1323 Globaux (netsh)",
                "disabled",
                DynamicParameterType.Enumeration,
                [
                    new() { RegistryValue = "disabled", DisplayName = "disabled (Désactivé - 12 octets/paquet économisés)" },
                    new() { RegistryValue = "enabled", DisplayName = "enabled (Activé)" }
                ], "TCP Kernel");

            AddOrUpdateParam(list, "TCPEcnCapability", "Capacité ECN Globale (netsh)",
                "disabled",
                DynamicParameterType.Enumeration,
                [
                    new() { RegistryValue = "disabled", DisplayName = "disabled (Désactivé - Évite routeurs incompatibles)" },
                    new() { RegistryValue = "enabled", DisplayName = "enabled (Activé)" }
                ], "TCP Kernel");

            AddOrUpdateParam(list, "MaxNegativeCacheTtl", "TTL Négatif Cache DNS (MaxNegativeCacheTtl)",
                "0",
                DynamicParameterType.Enumeration,
                [
                    new() { RegistryValue = "0", DisplayName = "0s (Réessai immédiat lors d'erreurs)" },
                    new() { RegistryValue = "5", DisplayName = "5s (Très court)" },
                    new() { RegistryValue = "300", DisplayName = "300s (Par défaut Windows)" }
                ], "DNS & Résolution");

            AddOrUpdateParam(list, "CacheHashTableBucketSize", "Taille Table Hachage DNS (CacheHashTableBucketSize)",
                "4",
                DynamicParameterType.Enumeration,
                [
                    new() { RegistryValue = "4", DisplayName = "4 (Haute capacité de hachage rapide)" },
                    new() { RegistryValue = "1", DisplayName = "1 (Standard)" }
                ], "DNS & Résolution");

            AddOrUpdateParam(list, "NetshIPv6Randomize", "Aléation Identifiants IPv6 (netsh)",
                "disabled",
                DynamicParameterType.Enumeration,
                [
                    new() { RegistryValue = "disabled", DisplayName = "disabled (Désactivé - Identifiant fixe stable)" },
                    new() { RegistryValue = "enabled", DisplayName = "enabled (Activé - Identifiant dynamique)" }
                ], "IPv6 & Tunneling");

            AddOrUpdateParam(list, "EnableAutoDnsOverHttps", "DNS over HTTPS - DoH (netsh / W11)",
                "2",
                DynamicParameterType.Enumeration,
                [
                    new() { RegistryValue = "2", DisplayName = "2 (DoH Exigé & Chiffré)" },
                    new() { RegistryValue = "1", DisplayName = "1 (DoH Opportuniste)" },
                    new() { RegistryValue = "0", DisplayName = "0 (Désactivé - DNS clair)" }
                ], "DNS & Résolution");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkDiscoveryService.AppendExtendedSystemParameters");
        }
    }

    private static void AddOrUpdateParam(
        List<DynamicNetworkParameter> list,
        string keyword,
        string displayName,
        string currentVal,
        DynamicParameterType type,
        List<DynamicParameterOption> options,
        string scope)
    {
        DynamicNetworkParameter? existing = list.FirstOrDefault(p => string.Equals(p.RegistryKeyword, keyword, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            list.Add(new DynamicNetworkParameter
            {
                RegistryKeyword = keyword,
                DisplayName = displayName,
                CurrentValue = currentVal,
                CurrentDisplayValue = currentVal,
                ValueType = type,
                ValidOptions = options,
                Scope = scope
            });
        }
    }

    public async Task<bool> RefreshWifiTelemetryAsync(NetworkAdapterHardwareDetails adapter, CancellationToken ct = default)
    {
        if (!adapter.IsWireless)
        {
            return false;
        }

        try
        {
            string output = await RunProcessOutputAsync("netsh.exe", "wlan show interfaces", ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(output))
            {
                return false;
            }

            string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = line.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        adapter.WifiSsid = parts[1].Trim();
                    }
                }
                else if (line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = line.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        adapter.WifiBssid = parts[1].Trim();
                    }
                }
                else if (line.StartsWith("Signal", StringComparison.OrdinalIgnoreCase))
                {
                    Match match = SignalPercentRegex.Match(line);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int sig))
                    {
                        adapter.WifiRssi = sig;
                    }
                }
                else if (line.StartsWith("Canal", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Channel", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = line.Split(':', 2);
                    if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int ch))
                    {
                        adapter.WifiChannel = ch;
                    }
                }
                else if (line.StartsWith("Type de radio", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Radio type", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = line.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        adapter.WifiRadioType = parts[1].Trim();
                    }
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkDiscoveryService.RefreshWifiTelemetryAsync");
            return false;
        }
    }

    private static Dictionary<string, (string DriverDesc, string DriverVersion, string DriverDate, string DriverProvider, string MatchingDeviceId, string PnpId)> GetPhysicalAdapterRegistryMetadata()
    {
        var dict = new Dictionary<string, (string, string, string, string, string, string)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using RegistryKey? classKey = Registry.LocalMachine.OpenSubKey(NetworkClassKeyPath);
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

                string matchId = sub.GetValue("MatchingDeviceId") as string ?? string.Empty;
                string pnpId = sub.GetValue("PnPInstanceId") as string ?? string.Empty;

                if (matchId.StartsWith("PCI", StringComparison.OrdinalIgnoreCase) ||
                    matchId.StartsWith("USB", StringComparison.OrdinalIgnoreCase) ||
                    pnpId.StartsWith("PCI", StringComparison.OrdinalIgnoreCase) ||
                    pnpId.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
                {
                    string desc = sub.GetValue("DriverDesc") as string ?? string.Empty;
                    string ver = sub.GetValue("DriverVersion") as string ?? string.Empty;
                    string date = sub.GetValue("DriverDate") as string ?? string.Empty;
                    string provider = sub.GetValue("ProviderName") as string ?? string.Empty;
                    dict[guid] = (desc, ver, date, provider, matchId, pnpId);
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkDiscoveryService.GetPhysicalAdapterRegistryMetadata");
        }
        return dict;
    }

    private static bool IsVirtualOrFilter(string name, string desc)
    {
        foreach (string f in ForbiddenVirtualKeywords)
        {
            if (desc.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(f, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static async Task<string> RunProcessOutputAsync(string fileName, string args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
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

            Task<string> outputTask = proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            return await outputTask.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }
}