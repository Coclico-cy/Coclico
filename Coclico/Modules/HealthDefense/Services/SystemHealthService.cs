using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Coclico.Services;

public sealed class SystemHealthService
{
    private const string ElevationRequiredSummary =
        "Cette opération nécessite les droits administrateur. Relancez Coclico en tant qu'administrateur (bouton d'élévation dans la barre de titre) puis réessayez.";

    public sealed record DefenderStatusInfo(
        bool IsAntivirusActive,
        bool IsRealTimeProtectionOn,
        string SignatureVersion,
        DateTime? LastUpdatedTime
    );

    public sealed record DiagnosticResult(
        bool Success,
        string Summary,
        string Details
    );

    private static bool RejectWithoutAdmin(Action<string>? onOutput, out DiagnosticResult result)
    {
        if (App.IsRunningAsAdministrator())
        {
            result = new DiagnosticResult(false, string.Empty, string.Empty);
            return false;
        }

        onOutput?.Invoke("❌ " + ElevationRequiredSummary);
        result = new DiagnosticResult(false, ElevationRequiredSummary, string.Empty);
        return true;
    }

    public async Task<DefenderStatusInfo> GetDefenderStatusAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                string script = "$s = Get-MpComputerStatus; [PSCustomObject]@{ Active = $s.AntivirusEnabled; Realtime = $s.RealTimeProtectionEnabled; Version = $s.AntivirusSignatureVersion; Updated = $s.AntivirusSignatureLastUpdated } | ConvertTo-Json -Compress";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return new DefenderStatusInfo(false, false, "Statut inconnu (PowerShell indisponible)", null);
                }

                string output = proc.StandardOutput.ReadToEnd();
                _ = proc.WaitForExit(8000);

                if (string.IsNullOrWhiteSpace(output))
                {
                    return new DefenderStatusInfo(false, false, "Statut inconnu (aucune réponse de Defender)", null);
                }

                using var doc = JsonDocument.Parse(output);
                JsonElement root = doc.RootElement;
                bool active = root.TryGetProperty("Active", out JsonElement pAct) && pAct.GetBoolean();
                bool realtime = root.TryGetProperty("Realtime", out JsonElement pRt) && pRt.GetBoolean();
                string version = root.TryGetProperty("Version", out JsonElement pVer) ? pVer.GetString() ?? "1.0" : "1.0";
                DateTime? updated = null;
                if (root.TryGetProperty("Updated", out JsonElement pUpd) && DateTime.TryParse(pUpd.GetString(), out DateTime d))
                {
                    updated = d;
                }

                return new DefenderStatusInfo(active, realtime, version, updated);
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "SystemHealthService.GetDefenderStatusAsync");
                return new DefenderStatusInfo(false, false, "Statut inconnu (erreur de lecture)", null);
            }
        });
    }

    public async Task<DiagnosticResult> UpdateDefenderSignaturesAsync(Action<string>? onOutput = null, CancellationToken ct = default)
    {
        if (RejectWithoutAdmin(onOutput, out DiagnosticResult denied))
        {
            return denied;
        }

        onOutput?.Invoke("🔄 Recherche et application des dernières définitions Microsoft Defender...");
        return await ExecutePowerShellCommandAsync("Update-MpSignature", onOutput, ct);
    }

    public async Task<DiagnosticResult> RunQuickDefenderScanAsync(Action<string>? onOutput = null, CancellationToken ct = default)
    {
        if (RejectWithoutAdmin(onOutput, out DiagnosticResult denied))
        {
            return denied;
        }

        onOutput?.Invoke("🛡️ Démarrage de l'analyse rapide de sécurité Windows Defender...");
        return await ExecutePowerShellCommandAsync("Start-MpScan -ScanType QuickScan", onOutput, ct);
    }

    public async Task<DiagnosticResult> RunFullDefenderScanAsync(Action<string>? onOutput = null, CancellationToken ct = default)
    {
        if (RejectWithoutAdmin(onOutput, out DiagnosticResult denied))
        {
            return denied;
        }

        onOutput?.Invoke("🛡️ Démarrage de l'analyse complète du système Windows Defender...");
        return await ExecutePowerShellCommandAsync("Start-MpScan -ScanType FullScan", onOutput, ct);
    }

    public async Task<bool> LaunchMrtAsync(bool quiet = false)
    {
        return await Task.Run(() =>
        {
            try
            {
                string mrtPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "system32", "MRT.exe");
                if (!File.Exists(mrtPath))
                {
                    mrtPath = "mrt.exe";
                }

                var psi = new ProcessStartInfo
                {
                    FileName = mrtPath,
                    Arguments = quiet ? "/q" : "",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using var proc = Process.Start(psi);
                return proc != null;
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "SystemHealthService.LaunchMrtAsync");
                return false;
            }
        });
    }

    public async Task<DiagnosticResult> ScheduleOfflineDefenderScanAsync(Action<string>? onOutput = null, CancellationToken ct = default)
    {
        if (RejectWithoutAdmin(onOutput, out DiagnosticResult denied))
        {
            return denied;
        }

        onOutput?.Invoke("⚠️ Programmation de l'analyse hors-ligne Windows Defender (WDO)...");
        return await ExecutePowerShellCommandAsync("Start-MpWDOScan", onOutput, ct);
    }

    public async Task<DiagnosticResult> RunSfcScanAsync(Action<string>? onOutput = null, CancellationToken ct = default)
    {
        if (RejectWithoutAdmin(onOutput, out DiagnosticResult denied))
        {
            return denied;
        }

        onOutput?.Invoke("🔍 Lancement de la vérification d'intégrité SFC (System File Checker)...");
        return await RunProcessAsync("sfc.exe", "/scannow", onOutput, ct);
    }

    public async Task<DiagnosticResult> RunDismScanHealthAsync(Action<string>? onOutput = null, CancellationToken ct = default)
    {
        if (RejectWithoutAdmin(onOutput, out DiagnosticResult denied))
        {
            return denied;
        }

        onOutput?.Invoke("🔍 Vérification de l'image Windows avec DISM /ScanHealth...");
        return await RunProcessAsync("dism.exe", "/Online /Cleanup-Image /ScanHealth", onOutput, ct);
    }

    public async Task<DiagnosticResult> RunDismRestoreHealthAsync(Action<string>? onOutput = null, CancellationToken ct = default)
    {
        if (RejectWithoutAdmin(onOutput, out DiagnosticResult denied))
        {
            return denied;
        }

        onOutput?.Invoke("🛠️ Réparation des composants Windows avec DISM /RestoreHealth...");
        return await RunProcessAsync("dism.exe", "/Online /Cleanup-Image /RestoreHealth", onOutput, ct);
    }

    public async Task<DiagnosticResult> RunDismComponentCleanupAsync(Action<string>? onOutput = null, CancellationToken ct = default)
    {
        if (RejectWithoutAdmin(onOutput, out DiagnosticResult denied))
        {
            return denied;
        }

        onOutput?.Invoke("🧹 Nettoyage du magasin des composants Windows (WinSxS)...");
        return await RunProcessAsync("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup", onOutput, ct);
    }

    public async Task<DiagnosticResult> RunChkdskScanAsync(Action<string>? onOutput = null, CancellationToken ct = default)
    {
        if (RejectWithoutAdmin(onOutput, out DiagnosticResult denied))
        {
            return denied;
        }

        onOutput?.Invoke("💾 Analyse de l'intégrité du système de fichiers NTFS avec CHKDSK /scan...");
        return await RunProcessAsync("chkdsk.exe", "C: /scan", onOutput, ct);
    }

    public async Task<DiagnosticResult> ResetNetworkStackAsync(Action<string>? onOutput = null, CancellationToken ct = default)
    {
        if (RejectWithoutAdmin(onOutput, out DiagnosticResult denied))
        {
            return denied;
        }

        onOutput?.Invoke("🌐 Réinitialisation de la pile réseau et Winsock...");
        var sb = new StringBuilder();

        DiagnosticResult r1 = await RunProcessAsync("netsh.exe", "winsock reset", onOutput, ct);
        _ = sb.AppendLine(r1.Summary);

        DiagnosticResult r2 = await RunProcessAsync("netsh.exe", "int ip reset", onOutput, ct);
        _ = sb.AppendLine(r2.Summary);

        DiagnosticResult r3 = await RunProcessAsync("ipconfig.exe", "/flushdns", onOutput, ct);
        _ = sb.AppendLine(r3.Summary);

        return new DiagnosticResult(r1.Success && r2.Success, "Pile réseau et DNS réinitialisés avec succès.", sb.ToString());
    }

    public async Task<DiagnosticResult> ResetWindowsUpdateAsync(Action<string>? onOutput = null, CancellationToken ct = default)
    {
        if (RejectWithoutAdmin(onOutput, out DiagnosticResult denied))
        {
            return denied;
        }

        onOutput?.Invoke("🔄 Réinitialisation des composants Windows Update...");
        var sb = new StringBuilder();

        DiagnosticResult stopWuau = await RunProcessAsync("net.exe", "stop wuauserv", onOutput, ct);
        _ = sb.AppendLine($"stop wuauserv: {stopWuau.Summary}");
        DiagnosticResult stopBits = await RunProcessAsync("net.exe", "stop bits", onOutput, ct);
        _ = sb.AppendLine($"stop bits: {stopBits.Summary}");

        try
        {
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string softDist = Path.Combine(winDir, "SoftwareDistribution", "Download");
            if (Directory.Exists(softDist))
            {
                int deleted = 0, locked = 0;
                foreach (string f in Directory.EnumerateFiles(softDist))
                {
                    try { File.Delete(f); deleted++; }
                    catch { locked++; }
                }
                onOutput?.Invoke($"🗑️ Cache de téléchargement Windows Update purgé ({deleted} fichiers supprimés, {locked} verrouillés).");
                _ = sb.AppendLine($"Purge SoftwareDistribution\\Download : {deleted} supprimés, {locked} verrouillés.");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "ResetWindowsUpdate.Purge");
            _ = sb.AppendLine($"Purge échouée : {ex.Message}");
        }

        DiagnosticResult startBits = await RunProcessAsync("net.exe", "start bits", onOutput, ct);
        _ = sb.AppendLine($"start bits: {startBits.Summary}");
        DiagnosticResult startWuau = await RunProcessAsync("net.exe", "start wuauserv", onOutput, ct);
        _ = sb.AppendLine($"start wuau: {startWuau.Summary}");

        bool success = startBits.Success && startWuau.Success;
        string summary = success
            ? "Services et cache Windows Update réinitialisés avec succès."
            : "Échec du redémarrage des services Windows Update (BITS/WUAU) — consultez les détails.";
        return new DiagnosticResult(success, summary, sb.ToString());
    }

    public async Task<DiagnosticResult> RunFullSystemRepairAsync(Action<string>? onOutput = null, Action<int, string>? onStep = null, CancellationToken ct = default)
    {
        if (RejectWithoutAdmin(onOutput, out DiagnosticResult denied))
        {
            return denied;
        }

        var sb = new StringBuilder();
        onOutput?.Invoke("=================================================");
        onOutput?.Invoke("🚀 DÉMARRAGE DE LA RÉPARATION INTÉGRALE WINDOWS 1-CLIC");
        onOutput?.Invoke("=================================================");

        onStep?.Invoke(1, "Mise à jour des définitions de sécurité...");
        DiagnosticResult defRes = await UpdateDefenderSignaturesAsync(onOutput, ct);
        _ = sb.AppendLine($"[1/4] Définitions Defender: {defRes.Summary}");

        onStep?.Invoke(2, "Réparation du magasin de composants DISM...");
        DiagnosticResult dismRes = await RunDismRestoreHealthAsync(onOutput, ct);
        _ = sb.AppendLine($"[2/4] DISM RestoreHealth: {dismRes.Summary}");

        onStep?.Invoke(3, "Vérification et réparation des fichiers système SFC...");
        DiagnosticResult sfcRes = await RunSfcScanAsync(onOutput, ct);
        _ = sb.AppendLine($"[3/4] SFC Scannow: {sfcRes.Summary}");

        onStep?.Invoke(4, "Optimisation réseau et purge du cache DNS...");
        DiagnosticResult netRes = await ResetNetworkStackAsync(onOutput, ct);
        _ = sb.AppendLine($"[4/4] Réseau/DNS: {netRes.Summary}");

        bool overallSuccess = defRes.Success && dismRes.Success && sfcRes.Success && netRes.Success;

        onOutput?.Invoke("=================================================");
        if (overallSuccess)
        {
            onOutput?.Invoke("✅ RÉPARATION INTÉGRALE TERMINÉE AVEC SUCCÈS !");
            onOutput?.Invoke("Votre système Windows a été analysé, réparé et remis à neuf.");
        }
        else
        {
            onOutput?.Invoke("⚠️ RÉPARATION INTÉGRALE TERMINÉE AVEC DES ERREURS.");
            onOutput?.Invoke("Certaines étapes ont échoué — consultez le détail ci-dessus pour identifier lesquelles.");
        }
        onOutput?.Invoke("=================================================");

        return new DiagnosticResult(
            overallSuccess,
            overallSuccess
                ? "Système réparé et stabilisé avec succès."
                : "Réparation terminée avec des erreurs sur une ou plusieurs étapes (voir le journal).",
            sb.ToString());
    }

    private async Task<DiagnosticResult> ExecutePowerShellCommandAsync(string script, Action<string>? onOutput, CancellationToken ct)
    {
        return await Task.Run(async () =>
        {
            var sb = new StringBuilder();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var proc = new Process { StartInfo = psi };
                proc.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        _ = sb.AppendLine(e.Data);
                        onOutput?.Invoke(e.Data);
                    }
                };
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        _ = sb.AppendLine($"[ERR] {e.Data}");
                        onOutput?.Invoke($"⚠️ {e.Data}");
                    }
                };

                _ = proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                try
                {
                    await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(entireProcessTree: true); } catch (Exception ex) { LoggingService.LogException(ex, "SystemHealthService.ExecutePowerShellCommandAsync"); }
                    throw;
                }

                bool success = proc.ExitCode == 0;
                string summary = success ? "Commande exécutée avec succès." : $"Commande terminée avec code {proc.ExitCode}.";
                return new DiagnosticResult(success, summary, sb.ToString());
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, $"ExecutePowerShell({script})");
                return new DiagnosticResult(false, ex.Message, ex.ToString());
            }
        }, ct);
    }

    private async Task<DiagnosticResult> RunProcessAsync(string exe, string args, Action<string>? onOutput, CancellationToken ct)
    {
        return await Task.Run(async () =>
        {
            var sb = new StringBuilder();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var proc = new Process { StartInfo = psi };
                proc.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        _ = sb.AppendLine(e.Data);
                        onOutput?.Invoke(e.Data);
                    }
                };
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        _ = sb.AppendLine($"[ERR] {e.Data}");
                        onOutput?.Invoke($"⚠️ {e.Data}");
                    }
                };

                _ = proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                try
                {
                    await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(entireProcessTree: true); } catch (Exception ex) { LoggingService.LogException(ex, "SystemHealthService.RunProcessAsync"); }
                    throw;
                }

                bool success = proc.ExitCode == 0;
                string summary = success ? "Opération terminée sans erreur." : $"Terminé avec code {proc.ExitCode}.";
                return new DiagnosticResult(success, summary, sb.ToString());
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, $"RunProcess({exe} {args})");
                return new DiagnosticResult(false, ex.Message, ex.ToString());
            }
        }, ct);
    }
}