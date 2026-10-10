using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.Json;
using Coclico.Models.Network;
using Microsoft.Win32;

namespace Coclico.Services.Network;

[SupportedOSPlatform("windows")]
public sealed class NetworkSnapshotService : INetworkSnapshotService
{
    private const string NetworkClassKeyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
    private static readonly string SnapshotFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Coclico", "snapshots", "network");
    private readonly List<NetworkConfigurationSnapshot> _storedSnapshots = [];
    private readonly object _lock = new();

    public NetworkConfigurationSnapshot? InitialBaselineSnapshot { get; private set; }

    public NetworkSnapshotService()
    {
        try
        {
            if (!Directory.Exists(SnapshotFolder))
            {
                _ = Directory.CreateDirectory(SnapshotFolder);
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkSnapshotService.Constructor");
        }
    }

    public async Task<NetworkConfigurationSnapshot> CaptureSnapshotAsync(
        string adapterIdOrGuid,
        string adapterName,
        string label,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var snapshot = new NetworkConfigurationSnapshot
            {
                AdapterId = adapterIdOrGuid,
                AdapterName = adapterName,
                Label = label
            };

            try
            {
                using RegistryKey? sub = OpenAdapterRegistryKey(adapterIdOrGuid, writable: false);
                if (sub != null)
                {
                    foreach (string valName in sub.GetValueNames())
                    {
                        string? val = sub.GetValue(valName)?.ToString();
                        if (val != null)
                        {
                            snapshot.NicRegistrySettings[valName] = val;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "NetworkSnapshotService.CaptureSnapshotAsync:NIC");
            }

            try
            {
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                NetworkInterface? ni = interfaces.FirstOrDefault(i => string.Equals(i.Id, adapterIdOrGuid, StringComparison.OrdinalIgnoreCase) ||
                                                       string.Equals(i.Name, adapterName, StringComparison.OrdinalIgnoreCase));
                if (ni != null)
                {
                    IPInterfaceProperties ipProps = ni.GetIPProperties();
                    try
                    {
                        IPv4InterfaceProperties v4 = ipProps.GetIPv4Properties();
                        if (v4 != null)
                        {
                            snapshot.Mtu = v4.Mtu;
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogException(ex, "NetworkSnapshotService.CaptureSnapshotAsync:Mtu");
                    }

                    snapshot.DnsServers = ipProps.DnsAddresses
                        .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                        .Select(d => d.ToString())
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "NetworkSnapshotService.CaptureSnapshotAsync:Interface");
            }

            try
            {
                var tcp = new TcpGlobalSettings();
                using RegistryKey? sysProfile = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", writable: false);
                if (sysProfile != null)
                {
                    if (uint.TryParse(sysProfile.GetValue("NetworkThrottlingIndex")?.ToString(), out uint nti))
                    {
                        tcp.NetworkThrottlingIndex = nti;
                    }

                    if (uint.TryParse(sysProfile.GetValue("SystemResponsiveness")?.ToString(), out uint sr))
                    {
                        tcp.SystemResponsiveness = sr;
                    }
                }

                using RegistryKey? ifKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{adapterIdOrGuid}", writable: false);
                if (ifKey != null)
                {
                    if (int.TryParse(ifKey.GetValue("TcpAckFrequency")?.ToString(), out int ack))
                    {
                        tcp.TcpAckFrequency = ack;
                    }

                    if (int.TryParse(ifKey.GetValue("TCPNoDelay")?.ToString(), out int nd))
                    {
                        tcp.TcpNoDelay = nd;
                    }
                }
                snapshot.TcpSettings = tcp;
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "NetworkSnapshotService.CaptureSnapshotAsync:Tcp");
            }

            lock (_lock)
            {
                InitialBaselineSnapshot ??= snapshot;
                _storedSnapshots.Add(snapshot);
            }

            try
            {
                string filePath = Path.Combine(SnapshotFolder, $"{snapshot.SnapshotId}.json");
                string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                string tmp = filePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "NetworkSnapshotService.CaptureSnapshotAsync:Save");
            }

            return snapshot;
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> RollbackToSnapshotAsync(NetworkConfigurationSnapshot snapshot, CancellationToken ct = default)
    {
        return await Task.Run(async () =>
        {
            try
            {
                using (RegistryKey? sub = OpenAdapterRegistryKey(snapshot.AdapterId, writable: true))
                {
                    if (sub != null)
                    {
                        foreach ((string key, string val) in snapshot.NicRegistrySettings)
                        {
                            try
                            {
                                sub.SetValue(key, val, RegistryValueKind.String);
                            }
                            catch (Exception ex)
                            {
                                LoggingService.LogException(ex, "NetworkSnapshotService.RollbackToSnapshotAsync:Value");
                            }
                        }
                    }
                }

                using (RegistryKey? sysProfile = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", writable: true))
                {
                    sysProfile?.SetValue("NetworkThrottlingIndex", snapshot.TcpSettings.NetworkThrottlingIndex, RegistryValueKind.DWord);
                    sysProfile?.SetValue("SystemResponsiveness", snapshot.TcpSettings.SystemResponsiveness, RegistryValueKind.DWord);
                }

                using (RegistryKey? ifKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{snapshot.AdapterId}", writable: true))
                {
                    if (ifKey != null)
                    {
                        ifKey.SetValue("TcpAckFrequency", snapshot.TcpSettings.TcpAckFrequency, RegistryValueKind.DWord);
                        ifKey.SetValue("TCPNoDelay", snapshot.TcpSettings.TcpNoDelay, RegistryValueKind.DWord);
                    }
                }

                await RunProcessAsync("ipconfig.exe", "/flushdns", ct).ConfigureAwait(false);

                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "NetworkSnapshotService.RollbackToSnapshotAsync");
                return false;
            }
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> RollbackSingleParameterAsync(
        string adapterIdOrGuid,
        string keyword,
        string originalValue,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (keyword is "TCPNoDelay" or "TcpAckFrequency" or "TCPDelAckTicks" or "MTU" or "NetbiosOptions")
                {
                    using RegistryKey? ifKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{adapterIdOrGuid}", writable: true);
                    if (ifKey != null)
                    {
                        if (string.IsNullOrEmpty(originalValue))
                        {
                            ifKey.DeleteValue(keyword, throwOnMissingValue: false);
                        }
                        else if (int.TryParse(originalValue, out int iv))
                        {
                            ifKey.SetValue(keyword, iv, RegistryValueKind.DWord);
                        }
                        else
                        {
                            ifKey.SetValue(keyword, originalValue, RegistryValueKind.String);
                        }

                        return true;
                    }
                }
                else if (keyword is "NetworkThrottlingIndex" or "SystemResponsiveness")
                {
                    using RegistryKey? sysKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", writable: true);
                    if (sysKey != null)
                    {
                        if (string.IsNullOrEmpty(originalValue))
                        {
                            sysKey.DeleteValue(keyword, throwOnMissingValue: false);
                        }
                        else if (uint.TryParse(originalValue, out uint uv))
                        {
                            sysKey.SetValue(keyword, unchecked((int)uv), RegistryValueKind.DWord);
                        }

                        return true;
                    }
                }
                else if (keyword is "NonBestEffortLimit")
                {
                    using RegistryKey? pschedKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Psched", writable: true);
                    if (pschedKey != null)
                    {
                        RestoreIntValue(pschedKey, keyword, originalValue);

                        return true;
                    }
                }
                else if (keyword is "DefaultTTL" or "MaxUserPort" or "TcpTimedWaitDelay" or "EnablePMTUDiscovery" or
                                    "EnablePMTUBHDetect" or "SynAttackProtect" or "NonSackRttResiliency" or "DisableTaskOffload" or
                                    "EnableDCA" or "TCPMaxDataRetransmissions" or "InitialRto" or "MinRto" or "SackOpts" or
                                    "FastSendDatagramThreshold" or "DefaultTOSValue" or "DisableIPSourceRouting" or "EnableICMPRedirect" or "DoNotUseNLA" or
                                    "TCPChimney" or "NetDMA" or "FastOpen" or "TcpCreateAndConnectDataUnchecked" or "DisableDHCPMediaSense" or
                                    "TCPWindowSize" or "GlobalMaxTcpWindowSize" or "EcnCapability" or "Timestamps" or "Pacing" or
                                    "IGMPLevel" or "ArpRetryCount" or "ArpCacheLife" or "TcpInitialRTT")
                {
                    using RegistryKey? tcpKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", writable: true);
                    if (tcpKey != null)
                    {
                        RestoreIntValue(tcpKey, keyword, originalValue);

                        return true;
                    }
                }
                else if (keyword is "MaxConnectionsPerServer" or "MaxConnectionsPer1_0Server")
                {
                    using RegistryKey? inetKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings", writable: true);
                    if (inetKey != null)
                    {
                        RestoreIntValue(inetKey, keyword, originalValue);

                        return true;
                    }
                }
                else if (keyword is "DnsCacheEntries" or "MaxCacheEntryTtlLimit" or "NegativeCacheTime" or "NetFailureCacheTime")
                {
                    using RegistryKey? dnsKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", writable: true);
                    if (dnsKey != null)
                    {
                        RestoreIntValue(dnsKey, keyword, originalValue);

                        return true;
                    }
                }

                using RegistryKey? sub = OpenAdapterRegistryKey(adapterIdOrGuid, writable: true);
                if (sub != null)
                {
                    if (string.IsNullOrEmpty(originalValue))
                    {
                        sub.DeleteValue(keyword, throwOnMissingValue: false);
                    }
                    else
                    {
                        sub.SetValue(keyword, originalValue, RegistryValueKind.String);
                    }
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, $"NetworkSnapshotService.RollbackSingleParameterAsync:{keyword}");
                return false;
            }
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> RollbackToBaselineAsync(CancellationToken ct = default)
    {
        return InitialBaselineSnapshot != null && await RollbackToSnapshotAsync(InitialBaselineSnapshot, ct).ConfigureAwait(false);
    }

    public IReadOnlyList<NetworkConfigurationSnapshot> GetStoredSnapshots()
    {
        lock (_lock)
        {
            return _storedSnapshots.ToList();
        }
    }

    private static void RestoreIntValue(RegistryKey key, string keyword, string originalValue)
    {
        if (string.IsNullOrEmpty(originalValue))
        {
            key.DeleteValue(keyword, throwOnMissingValue: false);
        }
        else if (int.TryParse(originalValue, out int intValue))
        {
            key.SetValue(keyword, intValue, RegistryValueKind.DWord);
        }
    }

    private static RegistryKey? OpenAdapterRegistryKey(string interfaceGuid, bool writable)
    {
        try
        {
            RegistryKey? classKey = Registry.LocalMachine.OpenSubKey(NetworkClassKeyPath, writable);
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
                    return sub;
                }
                sub.Dispose();
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkSnapshotService.OpenAdapterRegistryKey");
        }
        return null;
    }

    private static async Task RunProcessAsync(string fileName, string args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkSnapshotService.RunProcessAsync");
        }
    }
}