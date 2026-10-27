using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows;
using Coclico.Models.Network;
using Microsoft.Win32;

namespace Coclico.Services.Network;

[SupportedOSPlatform("windows")]
public sealed class NetworkOptimizerService : INetworkOptimizerService
{
    public const int QuickCheckPingSamples = 10;
    public const int CompleteTestPingSamples = 40;

    private const string ElevationRequiredSummary =
        "Coclico doit être exécuté en tant qu'administrateur (bouton d'élévation dans la barre de titre) pour modifier les paramètres réseau.";

    private const string AdapterKeywords = "'*EEE','EnableGreenEthernet','GigaLite','AdvancedEEE','*FlowControl','*InterruptModeration'";

    private static readonly string BackupFilePath = Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Coclico",
        "network-tuning-backup.json");

    private static readonly string[] VirtualOrFilterKeywords =
    [
        "virtual", "filter", "scheduler", "teredo", "isatap", "6to4",
        "bluetooth", "miniport", "kernel debug", "wfp", "lightweight",
        "direct", "pseudo", "tunneling", "vpn", "wireguard", "tailscale",
        "tap-windows", "npcap", "pcap", "vmware", "hyper-v", "virtualbox",
        "débogueur", "connexion au réseau local*"
    ];

    private readonly INetworkBenchmarkService _benchmark;

    public NetworkOptimizerService(INetworkBenchmarkService? benchmarkService = null)
    {
        _benchmark = benchmarkService ?? new NetworkBenchmarkService();
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
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || IsVirtualOrFilter(ni.Name, ni.Description))
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

                    int mtu = 1500;
                    try
                    {
                        IPv4InterfaceProperties v4 = ipProps.GetIPv4Properties();
                        if (v4 != null)
                        {
                            mtu = v4.Mtu;
                        }
                    }
                    catch (Exception ex) when (ex is not System.OutOfMemoryException)
                    {
                        LoggingService.LogException(ex, "NetworkOptimizerService.GetNetworkAdaptersAsync:Mtu");
                    }

                    string mac = ni.GetPhysicalAddress().ToString();
                    if (mac.Length == 12)
                    {
                        mac = string.Join(":", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)));
                    }

                    _ = physicalGuids.TryGetValue(ni.Id, out (string DriverDesc, string DriverVersion, string PnpId) pnpMeta);

                    list.Add(new NetworkAdapterInfo
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
                        Mtu = mtu,
                        DriverDescription = pnpMeta.DriverDesc ?? ni.Description,
                        DriverVersion = pnpMeta.DriverVersion ?? string.Empty,
                        PnpInstanceId = pnpMeta.PnpId ?? string.Empty
                    });
                }
            }
            catch (Exception ex) when (ex is not System.OutOfMemoryException)
            {
                LoggingService.LogException(ex, "NetworkOptimizerService.GetNetworkAdaptersAsync");
            }

            return list
                .OrderByDescending(a => a.IsActive)
                .ThenBy(a => a.AdapterType == NetworkAdapterType.Ethernet ? 0 : 1)
                .ToList();
        }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NetworkTuningParameter>> GetTuningStatusAsync(string? targetAdapterId = null, CancellationToken ct = default)
    {
        IReadOnlyList<NetworkAdapterInfo> adapters = await GetNetworkAdaptersAsync(ct).ConfigureAwait(false);
        NetworkAdapterInfo? target = ResolveTargetAdapter(adapters, targetAdapterId);
        TcpGlobalSettings tcp = await GetTcpSettingsAsync(ct).ConfigureAwait(false);
        Dictionary<string, string> adapterProps = await GetAdapterPropertiesAsync(target, ct).ConfigureAwait(false);

        var list = new List<NetworkTuningParameter>();
        foreach (NetworkTuningParameter definition in NetworkTuningCatalog.Definitions)
        {
            NetworkTuningParameter p = CloneDefinition(definition);
            p.CurrentValue = ReadParameterValue(definition.Id, tcp, adapterProps);
            p.IsOptimal = IsOptimal(definition.Id, p.CurrentValue, tcp, adapterProps);
            list.Add(p);
        }

        return list;
    }

    public async Task<bool> CaptureBackupAsync(string? targetAdapterId = null, CancellationToken ct = default)
    {
        try
        {
            IReadOnlyList<NetworkTuningParameter> status = await GetTuningStatusAsync(targetAdapterId, ct).ConfigureAwait(false);
            NetworkAdapterInfo? target = ResolveTargetAdapter(await GetNetworkAdaptersAsync(ct).ConfigureAwait(false), targetAdapterId);

            var backup = new NetworkTuningBackup
            {
                CapturedAt = DateTime.Now,
                AdapterId = target?.Id ?? string.Empty
            };

            foreach (NetworkTuningParameter p in status)
            {
                backup.Values[p.Id] = p.CurrentValue == "--" ? null : p.CurrentValue;
            }

            string? dir = Path.GetDirectoryName(BackupFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(BackupFilePath, JsonSerializer.Serialize(backup), ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Text.Json.JsonException or System.FormatException)
        {
            LoggingService.LogException(ex, "NetworkOptimizerService.CaptureBackupAsync");
            return false;
        }
    }

    public async Task<NetworkTuningReport> ApplyTuningAsync(
        NetworkTuningMode mode,
        string? targetAdapterId = null,
        Action<string>? onLog = null,
        Action<double>? onProgress = null,
        CancellationToken ct = default)
    {
        var report = new NetworkTuningReport();
        Action<string> log = onLog ?? (_ => { });
        Action<double> progress = onProgress ?? (_ => { });

        if (!App.IsRunningAsAdministrator())
        {
            LoggingService.LogWarning("[NetworkOptimizerService] Réglage réseau refusé : élévation administrateur requise.");
            report.Summary = ElevationRequiredSummary;
            report.FailedCount = NetworkTuningCatalog.Definitions.Count;
            return report;
        }

        try
        {
            progress(2);
            bool backupCaptured = await CaptureBackupAsync(targetAdapterId, ct).ConfigureAwait(false);
            log(backupCaptured
                ? "Valeurs actuelles sauvegardées (restauration possible à tout moment)."
                : "Attention : la sauvegarde des valeurs actuelles a échoué.");
            progress(6);

            NetworkAdapterInfo? target = ResolveTargetAdapter(await GetNetworkAdaptersAsync(ct).ConfigureAwait(false), targetAdapterId);
            if (target == null)
            {
                report.Summary = "Aucune carte réseau physique détectée.";
                return report;
            }

            log($"Carte ciblée : {target.Name} ({target.DriverDescription}).");

            IReadOnlyList<NetworkTuningParameter> status = await GetTuningStatusAsync(target.Id, ct).ConfigureAwait(false);
            List<NetworkTuningParameter> unavailable = [.. status.Where(p => p.CurrentValue == "--")];
            List<NetworkTuningParameter> toApply = [.. status.Where(p => !p.IsOptimal && p.CurrentValue != "--")];
            report.AlreadyOptimalCount = status.Count - toApply.Count - unavailable.Count;

            foreach (NetworkTuningParameter unavailableParameter in unavailable)
            {
                log($"· {L(unavailableParameter.NameKey)} : non disponible sur cette carte, ignoré.");
            }

            log("Mesure initiale (débit descendant + montant, 40 pings)...");
            report.Before = await RunCompleteTestAsync(4, null, p => progress(6 + p * 0.24), ct).ConfigureAwait(false);
            log($"Avant : ↓{report.Before.DownloadDisplay} · ↑{report.Before.UploadDisplay} · {report.Before.LatencyDisplay} · gigue {report.Before.JitterDisplay}");
            progress(32);

            if (toApply.Count == 0)
            {
                log("Tous les paramètres sont déjà à la valeur recommandée.");
                progress(100);
                report.After = report.Before;
                report.Summary = unavailable.Count == 0
                    ? "Réseau déjà optimal : aucun paramètre à modifier."
                    : $"Réseau déjà optimal : aucun paramètre à modifier ({unavailable.Count} non disponible(s) sur cette carte).";
                return report;
            }

            double perParameterSlice = mode == NetworkTuningMode.AutoTested ? 24.0 / toApply.Count : 38.0 / toApply.Count;
            double done = 32;

            foreach (NetworkTuningParameter p in toApply)
            {
                ct.ThrowIfCancellationRequested();
                bool ok = await ApplyParameterValueAsync(p.Id, p.TargetValue, target, ct).ConfigureAwait(false);
                if (ok)
                {
                    report.AppliedCount++;
                    log($"✓ {L(p.NameKey)} : {p.CurrentValue} → {p.TargetValue}");
                }
                else
                {
                    report.FailedCount++;
                    log($"✗ {L(p.NameKey)} : application impossible");
                }

                done += perParameterSlice;
                progress(Math.Min(70, done));

                if (mode == NetworkTuningMode.AutoTested && ok)
                {
                    (double pingMs, double jitterMs, double loss) = await MeasureLatencyAsync(count: QuickCheckPingSamples, ct: ct).ConfigureAwait(false);
                    log($"   contrôle : {pingMs:F1} ms ±{jitterMs:F1} (perte {loss:F0} %)");
                }
            }

            log("Mesure finale (débit descendant + montant, 40 pings)...");
            report.After = await RunCompleteTestAsync(4, null, p => progress(70 + p * 0.28), ct).ConfigureAwait(false);
            log($"Après : ↓{report.After.DownloadDisplay} · ↑{report.After.UploadDisplay} · {report.After.LatencyDisplay} · gigue {report.After.JitterDisplay}");
            progress(100);

            if (mode == NetworkTuningMode.AutoTested && IsRegression(report.Before, report.After))
            {
                log("Régression détectée : restauration des valeurs d'origine...");
                bool restored = await RestoreInitialValuesAsync(target.Id, onLog: null, ct).ConfigureAwait(false);
                report.RolledBack = restored;
                log(restored
                    ? "Retour arrière effectué : les valeurs d'origine ont été restaurées."
                    : "Échec du retour arrière : restaurez manuellement via le bouton dédié.");
                report.Summary = "Test final défavorable : retour arrière automatique effectué.";
            }
            else
            {
                report.Summary = $"{report.AppliedCount} paramètre(s) appliqué(s), {report.AlreadyOptimalCount} déjà optimal(aux), {report.FailedCount} échec(s).";
            }
        }
        catch (OperationCanceledException)
        {
            report.Summary = "Réglage annulé.";
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "NetworkOptimizerService.ApplyTuningAsync");
            report.Summary = "Erreur pendant le réglage : " + ex.Message;
        }

        return report;
    }

    public async Task<bool> RestoreInitialValuesAsync(string? targetAdapterId = null, Action<string>? onLog = null, CancellationToken ct = default)
    {
        if (!App.IsRunningAsAdministrator())
        {
            LoggingService.LogWarning("[NetworkOptimizerService] Restauration refusée : élévation administrateur requise.");
            return false;
        }

        try
        {
            if (!File.Exists(BackupFilePath))
            {
                onLog?.Invoke("Aucune sauvegarde trouvée : rien à restaurer.");
                return false;
            }

            NetworkTuningBackup? backup = JsonSerializer.Deserialize<NetworkTuningBackup>(await File.ReadAllTextAsync(BackupFilePath, ct).ConfigureAwait(false));
            if (backup is null || backup.Values.Count == 0)
            {
                return false;
            }

            NetworkAdapterInfo? target = ResolveTargetAdapter(await GetNetworkAdaptersAsync(ct).ConfigureAwait(false), targetAdapterId ?? backup.AdapterId);
            if (target == null)
            {
                return false;
            }

            int restored = 0;
            foreach (KeyValuePair<string, string?> entry in backup.Values)
            {
                if (entry.Value is null || entry.Value == "--")
                {
                    continue;
                }

                if (await ApplyParameterValueAsync(entry.Key, entry.Value, target, ct).ConfigureAwait(false))
                {
                    restored++;
                }
            }

            _ = await RunProcessAsync("ipconfig.exe", "/flushdns", ct).ConfigureAwait(false);
            onLog?.Invoke($"{restored} valeur(s) d'origine restaurée(s).");
            return restored > 0;
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Text.Json.JsonException or System.FormatException)
        {
            LoggingService.LogException(ex, "NetworkOptimizerService.RestoreInitialValuesAsync");
            return false;
        }
    }

    public async Task<bool> RestoreWindowsDefaultsAsync(string? targetAdapterId = null, Action<string>? onLog = null, CancellationToken ct = default)
    {
        if (!App.IsRunningAsAdministrator())
        {
            LoggingService.LogWarning("[NetworkOptimizerService] Restauration Windows refusée : élévation administrateur requise.");
            return false;
        }

        try
        {
            NetworkAdapterInfo? target = ResolveTargetAdapter(await GetNetworkAdaptersAsync(ct).ConfigureAwait(false), targetAdapterId);
            if (target == null)
            {
                return false;
            }

            _ = await RunProcessAsync("netsh.exe", $"int tcp set global autotuninglevel={NetworkTuningCatalog.AutoTuningTarget}", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", "int tcp set heuristics wsh=default", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", $"int tcp set global ecncapability={NetworkTuningCatalog.EcnCapabilityTarget}", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", $"int tcp set global timestamps={NetworkTuningCatalog.TimestampsTarget}", ct).ConfigureAwait(false);
            _ = await RunProcessAsync("netsh.exe", $"int tcp set global rss={NetworkTuningCatalog.RssTarget}", ct).ConfigureAwait(false);

            _ = await ApplyAdvancedPropertyAsync(target, "*EEE", "1", ct).ConfigureAwait(false);
            _ = await ApplyAdvancedPropertyAsync(target, "EnableGreenEthernet", "1", ct).ConfigureAwait(false);
            _ = await ApplyAdvancedPropertyAsync(target, "GigaLite", "1", ct).ConfigureAwait(false);
            _ = await ApplyAdvancedPropertyAsync(target, "AdvancedEEE", "1", ct).ConfigureAwait(false);
            _ = await ApplyAdvancedPropertyAsync(target, "*FlowControl", "3", ct).ConfigureAwait(false);
            _ = await ApplyAdvancedPropertyAsync(target, "*InterruptModeration", "1", ct).ConfigureAwait(false);
            _ = await ApplyAdapterPowerManagementAsync(target, "Enabled", ct).ConfigureAwait(false);

            _ = await RunProcessAsync("ipconfig.exe", "/flushdns", ct).ConfigureAwait(false);
            onLog?.Invoke("Paramètres d'origine Windows restaurés.");
            return true;
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "NetworkOptimizerService.RestoreWindowsDefaultsAsync");
            return false;
        }
    }

    public async Task<NetworkTestReport> RunCompleteTestAsync(int runs = 4, Action<string, int>? onRunProgress = null, Action<double>? onProgress = null, CancellationToken ct = default)
    {
        Action<double> progress = onProgress ?? (_ => { });

        var report = new NetworkTestReport
        {
            PingSamples = CompleteTestPingSamples
        };

        progress(5);
        (double median, double _, double jitter, double _, double loss) = await _benchmark
            .RunStatisticalPingAsync("1.1.1.1", CompleteTestPingSamples, 800, ct).ConfigureAwait(false);
        report.LatencyMs = median;
        report.JitterMs = jitter;
        report.PacketLossPercent = loss;

        progress(12);
        NetworkTestReport speed = await _benchmark.RunSpeedTestAsync(runs, 2, onRunProgress, ct).ConfigureAwait(false);
        report.DownloadMbps = speed.DownloadMbps;
        report.DownloadSucceeded = speed.DownloadSucceeded;
        report.DownloadRuns = speed.DownloadRuns;
        report.UploadMbps = speed.UploadMbps;
        report.UploadSucceeded = speed.UploadSucceeded;
        report.UploadRuns = speed.UploadRuns;

        progress(100);
        return report;
    }

    public async Task<(double PingMs, double JitterMs, double PacketLoss)> MeasureLatencyAsync(string targetHost = "1.1.1.1", int count = 10, CancellationToken ct = default)
    {
        (double median, double _, double jitter, double _, double loss) = await _benchmark
            .RunStatisticalPingAsync(targetHost, count, 800, ct).ConfigureAwait(false);
        return (median, jitter, loss);
    }

    private static string L(string key)
    {
        return Application.Current?.TryFindResource(key) as string ?? key;
    }

    internal async Task<TcpGlobalSettings> GetTcpSettingsAsync(CancellationToken ct = default)
    {
        var settings = new TcpGlobalSettings();
        try
        {
            string globalOutput = await RunProcessOutputAsync("netsh.exe", "int tcp show global", ct).ConfigureAwait(false);
            ApplyTcpLines(settings, globalOutput);
            string heuristicsOutput = await RunProcessOutputAsync("netsh.exe", "int tcp show heuristics", ct).ConfigureAwait(false);
            ApplyTcpLines(settings, heuristicsOutput);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "NetworkOptimizerService.GetTcpSettingsAsync");
        }

        return settings;
    }

    internal static void ApplyTcpLines(TcpGlobalSettings settings, string netshOutput)
    {
        foreach (string[] parts in netshOutput.Split('\n').Select(line => line.Split(':', 2)).Where(parts => parts.Length == 2))
        {
            string key = parts[0].Trim().ToLowerInvariant();
            string val = NormalizeNetshValue(parts[1]);

            if (key.Contains("réglage automatique") || key.Contains("autotuninglevel") || key.Contains("fenêtre de réception"))
            {
                settings.AutoTuningLevel = val;
            }
            else if (key.Contains("heuristique") || key.Contains("heuristics") || key.Contains("wsh"))
            {
                settings.ScalingHeuristics = val;
            }
            else if (key.Contains("capacité ecn") || key.Contains("ecncapability"))
            {
                settings.EcnCapability = val;
            }
            else if (key.Contains("horodatage") || key.Contains("timestamps"))
            {
                settings.Timestamps = val;
            }
            else if (key.Contains("rss") || key.Contains("receive side"))
            {
                settings.Rss = val;
            }
            else if (key.Contains("rsc") || key.Contains("coalescence"))
            {
                settings.Rsc = val;
            }
            else if (key.Contains("congestion"))
            {
                settings.CongestionProvider = val;
            }
        }
    }

    internal static string NormalizeNetshValue(string rawValue)
    {
        string v = rawValue.Trim().ToLowerInvariant();
        v = v.Replace("hautement restreint", "highlyrestricted").Replace("highly restricted", "highlyrestricted");
        v = v.Replace("restreint", "restricted");
        v = v.Replace("expérimental", "experimental");
        if (v.Contains("désactiv"))
        {
            return "disabled";
        }

        if (v.Contains("activ"))
        {
            return "enabled";
        }

        return v;
    }

    private async Task<Dictionary<string, string>> GetAdapterPropertiesAsync(NetworkAdapterInfo? target, CancellationToken ct = default)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (target == null || !NetworkSettingsGuard.IsSafeAdapterName(target.Name))
        {
            return props;
        }

        try
        {
            string name = NetworkSettingsGuard.EscapePowerShellSingleQuoted(target.Name);
            string script =
                "$a = Get-NetAdapterAdvancedProperty -Name '" + name + "' -RegistryKeyword " + AdapterKeywords + " -ErrorAction SilentlyContinue; " +
                "foreach ($p in $a) { Write-Output ($p.RegistryKeyword + '=' + (($p.RegistryValue -join ';'))) }; " +
                "$pm = Get-NetAdapterPowerManagement -Name '" + name + "' -ErrorAction SilentlyContinue; " +
                "if ($pm) { Write-Output ('POWERMGMT=' + [string]$pm.AllowComputerToTurnOffDevice) }";

            (int exit, string output) = await RunProcessWithExitAsync("powershell.exe", BuildPowerShellArguments(script), ct).ConfigureAwait(false);
            if (exit == 0)
            {
                foreach (string[] pair in output.Split('\n').Select(line => line.Split('=', 2)).Where(pair => pair.Length == 2 && pair[0].Trim().Length > 0))
                {
                    props[pair[0].Trim()] = pair[1].Trim();
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or System.IO.IOException or System.UnauthorizedAccessException)
        {
            LoggingService.LogException(ex, "NetworkOptimizerService.GetAdapterPropertiesAsync");
        }

        return props;
    }

    private static string BuildPowerShellArguments(string script)
    {
        return "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"";
    }

    private async Task<bool> ApplyAdvancedPropertyAsync(NetworkAdapterInfo target, string keyword, string value, CancellationToken ct = default)
    {
        if (!NetworkSettingsGuard.IsSafeAdapterName(target.Name))
        {
            return false;
        }

        string name = NetworkSettingsGuard.EscapePowerShellSingleQuoted(target.Name);
        string script =
            "$p = Get-NetAdapterAdvancedProperty -Name '" + name + "' -RegistryKeyword '" + keyword + "' -ErrorAction SilentlyContinue; " +
            "if (-not $p) { exit 2 }; " +
            "Set-NetAdapterAdvancedProperty -Name '" + name + "' -RegistryKeyword '" + keyword + "' -RegistryValue '" + NetworkSettingsGuard.EscapePowerShellSingleQuoted(value) + "'";

        (int exit, string output) = await RunProcessWithExitAsync("powershell.exe", BuildPowerShellArguments(script), ct).ConfigureAwait(false);
        if (exit != 0 && exit != 2)
        {
            LoggingService.LogWarning($"[NetworkOptimizerService] Set-NetAdapterAdvancedProperty '{keyword}' a échoué (code {exit}) : {output.Trim()}");
        }

        return exit == 0;
    }

    private async Task<bool> ApplyAdapterPowerManagementAsync(NetworkAdapterInfo target, string allowed, CancellationToken ct = default)
    {
        if (!NetworkSettingsGuard.IsSafeAdapterName(target.Name))
        {
            return false;
        }

        string name = NetworkSettingsGuard.EscapePowerShellSingleQuoted(target.Name);
        string script =
            "$pm = Get-NetAdapterPowerManagement -Name '" + name + "' -ErrorAction SilentlyContinue; " +
            "if (-not $pm) { exit 2 }; " +
            "$pm.AllowComputerToTurnOffDevice = '" + allowed + "'; " +
            "$pm | Set-NetAdapterPowerManagement";

        (int exit, string output) = await RunProcessWithExitAsync("powershell.exe", BuildPowerShellArguments(script), ct).ConfigureAwait(false);
        if (exit != 0 && exit != 2)
        {
            LoggingService.LogWarning($"[NetworkOptimizerService] Set-NetAdapterPowerManagement a échoué (code {exit}) : {output.Trim()}");
        }

        return exit == 0;
    }

    private async Task<bool> ApplyEeePropertiesAsync(NetworkAdapterInfo target, string? value, CancellationToken ct)
    {
        string eeeValue = value ?? NetworkTuningCatalog.AdapterEeeTarget;
        bool anyApplied = await ApplyAdvancedPropertyAsync(target, "*EEE", eeeValue, ct).ConfigureAwait(false);
        anyApplied |= await ApplyAdvancedPropertyAsync(target, "EnableGreenEthernet", eeeValue, ct).ConfigureAwait(false);
        anyApplied |= await ApplyAdvancedPropertyAsync(target, "GigaLite", eeeValue, ct).ConfigureAwait(false);
        anyApplied |= await ApplyAdvancedPropertyAsync(target, "AdvancedEEE", eeeValue, ct).ConfigureAwait(false);
        return anyApplied;
    }

    private static NetworkTuningParameter CloneDefinition(NetworkTuningParameter definition)
    {
        return new NetworkTuningParameter
        {
            Id = definition.Id,
            NameKey = definition.NameKey,
            DescriptionKey = definition.DescriptionKey,
            Category = definition.Category,
            TargetValue = definition.TargetValue
        };
    }

    private static string ReadParameterValue(string id, TcpGlobalSettings tcp, Dictionary<string, string> adapterProps)
    {
        switch (id)
        {
            case NetworkTuningCatalog.AutoTuningId:
                return tcp.AutoTuningLevel;
            case NetworkTuningCatalog.ScalingHeuristicsId:
                return tcp.ScalingHeuristics;
            case NetworkTuningCatalog.EcnCapabilityId:
                return tcp.EcnCapability;
            case NetworkTuningCatalog.TimestampsId:
                return tcp.Timestamps;
            case NetworkTuningCatalog.RssId:
                return tcp.Rss;
            case NetworkTuningCatalog.AdapterPowerId:
                return adapterProps.TryGetValue("POWERMGMT", out string? power) && power.Length > 0 ? power : "--";
            case NetworkTuningCatalog.AdapterEeeId:
                if (adapterProps.TryGetValue("*EEE", out string? eee) && eee.Length > 0)
                {
                    return eee.Split(';')[0];
                }

                if (adapterProps.TryGetValue("EnableGreenEthernet", out string? green) && green.Length > 0)
                {
                    return green.Split(';')[0];
                }

                return "--";
            case NetworkTuningCatalog.AdapterFlowControlId:
                return adapterProps.TryGetValue("*FlowControl", out string? flow) && flow.Length > 0 ? flow.Split(';')[0] : "--";
            case NetworkTuningCatalog.AdapterInterruptModerationId:
                return adapterProps.TryGetValue("*InterruptModeration", out string? moderation) && moderation.Length > 0
                    ? moderation.Split(';')[0]
                    : "--";
            default:
                return "--";
        }
    }

    private static bool IsOptimal(string id, string currentValue, TcpGlobalSettings tcp, Dictionary<string, string> adapterProps)
    {
        switch (id)
        {
            case NetworkTuningCatalog.AutoTuningId:
                return string.Equals(tcp.AutoTuningLevel, NetworkTuningCatalog.AutoTuningTarget, StringComparison.OrdinalIgnoreCase);
            case NetworkTuningCatalog.ScalingHeuristicsId:
                return string.Equals(tcp.ScalingHeuristics, NetworkTuningCatalog.ScalingHeuristicsTarget, StringComparison.OrdinalIgnoreCase);
            case NetworkTuningCatalog.EcnCapabilityId:
                return string.Equals(tcp.EcnCapability, NetworkTuningCatalog.EcnCapabilityTarget, StringComparison.OrdinalIgnoreCase);
            case NetworkTuningCatalog.TimestampsId:
                return string.Equals(tcp.Timestamps, NetworkTuningCatalog.TimestampsTarget, StringComparison.OrdinalIgnoreCase);
            case NetworkTuningCatalog.RssId:
                return string.Equals(tcp.Rss, NetworkTuningCatalog.RssTarget, StringComparison.OrdinalIgnoreCase);
            case NetworkTuningCatalog.AdapterPowerId:
                return string.Equals(currentValue, NetworkTuningCatalog.AdapterPowerTarget, StringComparison.OrdinalIgnoreCase);
            case NetworkTuningCatalog.AdapterEeeId:
                return string.Equals(currentValue, NetworkTuningCatalog.AdapterEeeTarget, StringComparison.OrdinalIgnoreCase);
            case NetworkTuningCatalog.AdapterFlowControlId:
                return string.Equals(currentValue, NetworkTuningCatalog.AdapterFlowControlTarget, StringComparison.OrdinalIgnoreCase);
            case NetworkTuningCatalog.AdapterInterruptModerationId:
                return string.Equals(currentValue, NetworkTuningCatalog.AdapterInterruptModerationTarget, StringComparison.OrdinalIgnoreCase);
            default:
                return false;
        }
    }

    private async Task<bool> ApplyParameterValueAsync(string id, string? value, NetworkAdapterInfo target, CancellationToken ct)
    {
        try
        {
            switch (id)
            {
                case NetworkTuningCatalog.AutoTuningId:
                    return (await RunProcessAsync("netsh.exe", $"int tcp set global autotuninglevel={value}", ct).ConfigureAwait(false)).Success;
                case NetworkTuningCatalog.ScalingHeuristicsId:
                    return (await RunProcessAsync("netsh.exe", $"int tcp set heuristics wsh={value} forcews=enabled", ct).ConfigureAwait(false)).Success;
                case NetworkTuningCatalog.EcnCapabilityId:
                    return (await RunProcessAsync("netsh.exe", $"int tcp set global ecncapability={value}", ct).ConfigureAwait(false)).Success;
                case NetworkTuningCatalog.TimestampsId:
                    return (await RunProcessAsync("netsh.exe", $"int tcp set global timestamps={value}", ct).ConfigureAwait(false)).Success;
                case NetworkTuningCatalog.RssId:
                    return (await RunProcessAsync("netsh.exe", $"int tcp set global rss={value}", ct).ConfigureAwait(false)).Success;
                case NetworkTuningCatalog.AdapterPowerId:
                    return await ApplyAdapterPowerManagementAsync(target, value ?? "Enabled", ct).ConfigureAwait(false);
                case NetworkTuningCatalog.AdapterEeeId:
                    return await ApplyEeePropertiesAsync(target, value, ct).ConfigureAwait(false);
                case NetworkTuningCatalog.AdapterFlowControlId:
                    return await ApplyAdvancedPropertyAsync(target, "*FlowControl", value ?? "3", ct).ConfigureAwait(false);
                case NetworkTuningCatalog.AdapterInterruptModerationId:
                    return await ApplyAdvancedPropertyAsync(target, "*InterruptModeration", value ?? "1", ct).ConfigureAwait(false);
                default:
                    return false;
            }
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "NetworkOptimizerService.ApplyParameterValueAsync:" + id);
            return false;
        }
    }

    private static bool IsRegression(NetworkTestReport? before, NetworkTestReport? after)
    {
        if (before == null || after == null)
        {
            return false;
        }

        bool latencyRegression = before.LatencyMs > 0 && before.LatencyMs < 900 &&
            after.LatencyMs > before.LatencyMs * 1.15;

        bool downloadRegression = before.DownloadSucceeded && after.DownloadSucceeded &&
            after.DownloadMbps < before.DownloadMbps * 0.85;

        bool packetLossRegression = after.PacketLossPercent > 5 && after.PacketLossPercent > before.PacketLossPercent * 2;

        return latencyRegression || downloadRegression || packetLossRegression;
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
        catch (Exception ex) when (ex is System.Security.SecurityException or System.IO.IOException or System.UnauthorizedAccessException)
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
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.InvalidOperationException)
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
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static async Task<(int ExitCode, string Output)> RunProcessWithExitAsync(string exe, string args, CancellationToken ct)
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
                return (-1, string.Empty);
            }

            Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderrTask = proc.StandardError.ReadToEndAsync(ct);
            Task exitTask = proc.WaitForExitAsync(ct);

            await Task.WhenAll(stdoutTask, stderrTask, exitTask).ConfigureAwait(false);
            string output = await stdoutTask.ConfigureAwait(false);
            string error = await stderrTask.ConfigureAwait(false);
            return (proc.ExitCode, !string.IsNullOrWhiteSpace(output) ? output : error);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.InvalidOperationException)
        {
            LoggingService.LogException(ex, "NetworkOptimizerService.RunProcessWithExitAsync");
            return (-1, ex.Message);
        }
    }
}
