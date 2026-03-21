using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Models;

namespace Coclico.Services
{
    public sealed class FlowChainExecutionService
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

        public sealed class ExecutionResult
        {
            public bool Success { get; init; }
            public int NodesExecuted { get; init; }
            public int NodesFailed { get; init; }
            public int NodesSkipped { get; init; }
            public TimeSpan ElapsedTime { get; init; }
            public string Summary { get; init; } = string.Empty;
        }

        public event Action<FlowItem, string>? NodeExecuting;
        public event Action<FlowItem, bool, string>? NodeCompleted;

        public async Task<ExecutionResult> ExecuteChainAsync(
            FlowChain chain,
            IProgress<(string status, int percent)>? progress = null,
            CancellationToken ct = default)
        {
            if (chain == null) return new ExecutionResult { Summary = "Chaîne nulle" };

            var featureName = $"FlowChain:{chain.Name}";
            return await FeatureExecutionEngine.Instance.RunFeatureAsync(featureName, async ctx =>
            {
                chain.IsRunning = true;
                var sw = Stopwatch.StartNew();
                int executed = 0, failed = 0, skipped = 0;

                var enabledItems = chain.Items.Where(i => i.IsEnabled).ToList();
                int total = enabledItems.Count;

                try
                {
                    ctx.Report($"Démarrage de l'exécution de la chaîne '{chain.Name}'");

                    for (int i = 0; i < enabledItems.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var item = enabledItems[i];
                        int percent = total > 0 ? (int)((double)(i + 1) / total * 100) : 0;

                        if (item.NodeType is NodeType.Start or NodeType.End)
                        {
                            progress?.Report(($"{item.NodeTypeLabel}: {item.Name}", percent));
                            executed++;
                            continue;
                        }

                        var phase = $"Node {i + 1}/{total}: {item.Name}";
                        progress?.Report(($"Exécution: {phase}...", percent));
                        NodeExecuting?.Invoke(item, $"Exécution de {item.NodeTypeLabel}...");
                        ctx.Report(phase);

                        bool success = false;
                        string message = string.Empty;
                        int attempts = 0;
                        int maxAttempts = Math.Max(1, item.RetryCount + 1);

                        while (attempts < maxAttempts && !success)
                        {
                            attempts++;
                            try
                            {
                                using var timeoutCts = item.TimeoutSeconds > 0
                                    ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                                    : null;
                                timeoutCts?.CancelAfter(TimeSpan.FromSeconds(item.TimeoutSeconds));
                                var effectiveCt = timeoutCts?.Token ?? ct;

                                success = await ExecuteNodeAsync(item, effectiveCt);
                                message = success ? "OK" : "Échoué";
                            }
                            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                            {
                                message = $"Timeout après {item.TimeoutSeconds}s";
                            }
                            catch (Exception ex)
                            {
                                message = ex.Message;
                                LoggingService.LogException(ex, $"FlowChainExec.{item.NodeType}.{item.Name}");
                            }

                            if (!success && attempts < maxAttempts)
                            {
                                await Task.Delay(1000, ct);
                                LoggingService.LogInfo($"Retry {attempts}/{maxAttempts} for {item.Name}");
                                ctx.Report($"Retry {attempts}/{maxAttempts}");
                            }
                        }

                        if (success) executed++;
                        else failed++;

                        NodeCompleted?.Invoke(item, success, message);
                        ctx.Report($"Node {item.Name} completed: {(success ? "OK" : "FAIL")}");
                    }
                }
                catch (OperationCanceledException)
                {
                    LoggingService.LogInfo($"Chain '{chain.Name}' cancelled.");
                    ctx.SetStatus(FeatureExecutionStatus.Warning, "Execution cancelled");
                }
                finally
                {
                    sw.Stop();
                    chain.IsRunning = false;
                    chain.LastRunTime = DateTime.UtcNow;
                    chain.LastRunStatus = failed == 0 ? "✅ Succès" : $"⚠️ {failed} erreur(s)";
                    ctx.Report($"Terminé: {executed} exécutés, {failed} échoués, {skipped} ignorés");
                }

                return new ExecutionResult
                {
                    Success = failed == 0,
                    NodesExecuted = executed,
                    NodesFailed = failed,
                    NodesSkipped = skipped,
                    ElapsedTime = sw.Elapsed,
                    Summary = $"Exécuté: {executed}, Échoué: {failed}, Ignoré: {skipped} — {sw.Elapsed.TotalSeconds:F1}s"
                };
            }).ConfigureAwait(false);
        }

        private async Task<bool> ExecuteNodeAsync(FlowItem item, CancellationToken ct)
        {
            return item.NodeType switch
            {
                NodeType.OpenApp            => await ExecuteOpenAppAsync(item, ct),
                NodeType.CloseApp           => ExecuteCloseApp(item),
                NodeType.KillProcess        => ExecuteCloseApp(item),
                NodeType.RunCommand         => await ExecuteRunCommandAsync(item, ct),
                NodeType.Delay              => await ExecuteDelayAsync(item, ct),
                NodeType.Condition          => EvaluateCondition(item),
                NodeType.Loop               => await ExecuteLoopAsync(item, ct),
                NodeType.Notification       => ExecuteNotification(item),
                NodeType.HttpRequest        => await ExecuteHttpRequestAsync(item, ct),
                NodeType.FileOperation      => ExecuteFileOperation(item),
                NodeType.SystemCheck        => await ExecuteSystemCheckAsync(ct),
                NodeType.RunPowerShell      => await ExecuteRunPowerShellAsync(item, ct),
                NodeType.OpenUrl            => ExecuteOpenUrl(item),
                NodeType.SetVolume          => ExecuteSetVolume(item),
                NodeType.MuteAudio          => ExecuteMuteAudio(),
                NodeType.SetProcessPriority => ExecuteSetProcessPriority(item),
                NodeType.KillByMemory       => ExecuteKillByMemory(),
                NodeType.CleanTemp          => await ExecuteCleanTempAsync(ct),
                NodeType.RamClean           => ExecuteRamClean(),
                NodeType.ClipboardSet       => ExecuteClipboardSet(item),
                NodeType.Screenshot         => await ExecuteScreenshotAsync(item, ct),
                NodeType.SendKeys           => ExecuteSendKeys(item),
                NodeType.CompressFile       => await ExecuteCompressFileAsync(item, ct),
                NodeType.EmptyRecycleBin    => ExecuteEmptyRecycleBin(),
                NodeType.TriggerShortcut    => ExecuteTriggerShortcut(item),
                _                           => true
            };
        }

        private static async Task<bool> ExecuteOpenAppAsync(FlowItem item, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(item.ProgramPath)) return false;
            if (!File.Exists(item.ProgramPath)) return false;

            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = item.ProgramPath,
                    Arguments = item.Arguments ?? string.Empty,
                    UseShellExecute = true
                }
            };
            proc.Start();
            if (item.WaitForPreviousExit)
                await proc.WaitForExitAsync(ct);
            return true;
        }

        private static bool ExecuteCloseApp(FlowItem item)
        {
            string? nameToKill = item.ProcessName;
            if (string.IsNullOrEmpty(nameToKill) && !string.IsNullOrEmpty(item.ProgramPath))
                nameToKill = Path.GetFileNameWithoutExtension(item.ProgramPath);
            if (string.IsNullOrEmpty(nameToKill)) return false;

            if (nameToKill.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                nameToKill = nameToKill[..^4];

            bool killed = false;
            foreach (var p in Process.GetProcessesByName(nameToKill))
            {
                using (p)
                {
                    try { p.Kill(); p.WaitForExit(3000); killed = true; } catch { }
                }
            }
            return killed;
        }

        private static readonly string[] _blockedCommandPatterns =
        [
            "format ",
            "format/",
            "rd /s /q",
            "rmdir /s /q",
            "del /f /s /q",
            "del /s /q",
            "rm -rf",
            "rm -r /",
            "cipher /w:",
            "bcdedit",
            "diskpart",
            "reg delete hklm",
            "reg delete hkcc",
            "net user",
            "net localgroup administrators",
            "icacls",
            "takeown",
            "cacls",
            "sc delete",
            "sc stop",
        ];

        private static string NormalizeForBlockCheck(string input)
            => System.Text.RegularExpressions.Regex.Replace(input.ToLowerInvariant(), @"\s+", " ").Trim();

        private static async Task<bool> ExecuteRunCommandAsync(FlowItem item, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(item.CommandLine)) return false;

            string normalizedCmd = NormalizeForBlockCheck(item.CommandLine);
            foreach (var blocked in _blockedCommandPatterns)
            {
                if (normalizedCmd.Contains(blocked))
                {
                    LoggingService.LogError($"Blocked dangerous command [{blocked}]: {item.CommandLine[..Math.Min(80, item.CommandLine.Length)]}");
                    return false;
                }
            }

            using var cmd = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {item.CommandLine}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            cmd.Start();
            if (item.WaitForPreviousExit) await cmd.WaitForExitAsync(ct);
            return true;
        }

        private static async Task<bool> ExecuteDelayAsync(FlowItem item, CancellationToken ct)
        {
            int ms = Math.Max(0, item.DelaySeconds) * 1000;
            if (ms > 0) await Task.Delay(ms, ct);
            return true;
        }

        private static bool EvaluateCondition(FlowItem item)
        {
            try
            {
                return item.ConditionOperator switch
                {
                    ConditionOperator.ProcessRunning =>
                        !string.IsNullOrEmpty(item.ConditionValue) &&
                        Process.GetProcessesByName(item.ConditionValue).Length > 0,

                    ConditionOperator.ProcessNotRunning =>
                        !string.IsNullOrEmpty(item.ConditionValue) &&
                        Process.GetProcessesByName(item.ConditionValue).Length == 0,

                    ConditionOperator.FileExists =>
                        !string.IsNullOrEmpty(item.ConditionValue) && File.Exists(item.ConditionValue),

                    ConditionOperator.FileNotExists =>
                        !string.IsNullOrEmpty(item.ConditionValue) && !File.Exists(item.ConditionValue),

                    ConditionOperator.TimeAfter =>
                        TimeSpan.TryParse(item.ConditionValue, out var after) &&
                        DateTime.Now.TimeOfDay > after,

                    ConditionOperator.TimeBefore =>
                        TimeSpan.TryParse(item.ConditionValue, out var before) &&
                        DateTime.Now.TimeOfDay < before,

                    _ => true
                };
            }
            catch { return false; }
        }

        private async Task<bool> ExecuteLoopAsync(FlowItem item, CancellationToken ct)
        {
            for (int i = 0; i < item.LoopCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (item.LoopDelayMs > 0)
                    await Task.Delay(item.LoopDelayMs, ct);
            }
            return true;
        }

        private static bool ExecuteNotification(FlowItem item)
        {
            if (string.IsNullOrEmpty(item.NotificationMessage)) return false;
            try
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    ToastService.Show(item.NotificationMessage));
                return true;
            }
            catch { return false; }
        }

        private static bool IsPrivateOrLoopbackHost(string host)
        {
            if (host is "localhost" or "::1" or "0.0.0.0") return true;

            if (System.Net.IPAddress.TryParse(host, out var ip))
            {
                byte[] b = ip.GetAddressBytes();
                if (b.Length == 4 && b[0] == 127) return true;
                if (b.Length == 4 && b[0] == 10) return true;
                if (b.Length == 4 && b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
                if (b.Length == 4 && b[0] == 192 && b[1] == 168) return true;
                if (b.Length == 4 && b[0] == 169 && b[1] == 254) return true;
                if (b.Length == 16 && (b[0] & 0xFE) == 0xFC) return true;
            }
            return false;
        }

        private static async Task<bool> ExecuteHttpRequestAsync(FlowItem item, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(item.HttpUrl)) return false;

            if (!Uri.TryCreate(item.HttpUrl, UriKind.Absolute, out var uri))
                return false;
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                return false;
            if (IsPrivateOrLoopbackHost(uri.Host))
            {
                LoggingService.LogError($"SSRF blocked: {uri.Host}");
                return false;
            }

            string method = (item.HttpMethod ?? "GET").ToUpperInvariant();
            var request = new HttpRequestMessage(new HttpMethod(method), uri);

            using var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }

        private static bool ExecuteFileOperation(FlowItem item)
        {
            if (string.IsNullOrEmpty(item.FileOperationSource)) return false;

            string opType = (item.FileOperationType ?? "copy").ToLowerInvariant();

            return opType switch
            {
                "copy" when !string.IsNullOrEmpty(item.FileOperationDest) =>
                    CopyFileSafe(item.FileOperationSource, item.FileOperationDest),
                "move" when !string.IsNullOrEmpty(item.FileOperationDest) =>
                    MoveFileSafe(item.FileOperationSource, item.FileOperationDest),
                "delete" => DeleteFileSafe(item.FileOperationSource),
                _ => false
            };
        }

        private static readonly string[] _protectedPathSegments =
        [
            @"\windows\",
            @"\program files\",
            @"\program files (x86)\",
            @"\programdata\microsoft\",
            @"\system volume information\",
            @"\$recycle.bin\",
            @"\recovery\",
            @"\boot\",
            @"\efi\",
        ];

        private static bool IsProtectedPath(string fullPath)
        {
            string pathWithoutDrive = fullPath.Length >= 2 && fullPath[1] == ':'
                ? fullPath[2..].ToLowerInvariant()
                : fullPath.ToLowerInvariant();

            if (!pathWithoutDrive.StartsWith('\\'))
                pathWithoutDrive = '\\' + pathWithoutDrive;

            foreach (var seg in _protectedPathSegments)
            {
                if (pathWithoutDrive.StartsWith(seg, StringComparison.Ordinal) ||
                    pathWithoutDrive.Contains(seg, StringComparison.Ordinal))
                    return true;
            }

            string coclicoData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico");
            if (fullPath.StartsWith(coclicoData, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static bool CopyFileSafe(string source, string dest)
        {
            try
            {
                string fullSource = Path.GetFullPath(source);
                string fullDest   = Path.GetFullPath(dest);

                if (IsProtectedPath(fullSource) || IsProtectedPath(fullDest)) return false;

                if (File.Exists(fullSource))
                {
                    string? destDir = Path.GetDirectoryName(fullDest);
                    if (destDir != null) Directory.CreateDirectory(destDir);
                    File.Copy(fullSource, fullDest, overwrite: true);
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        private static bool MoveFileSafe(string source, string dest)
        {
            try
            {
                string fullSource = Path.GetFullPath(source);
                string fullDest   = Path.GetFullPath(dest);

                if (IsProtectedPath(fullSource) || IsProtectedPath(fullDest)) return false;

                if (File.Exists(fullSource))
                {
                    string? destDir = Path.GetDirectoryName(fullDest);
                    if (destDir != null) Directory.CreateDirectory(destDir);
                    File.Move(fullSource, fullDest, overwrite: true);
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        private static bool DeleteFileSafe(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);

                if (IsProtectedPath(fullPath)) return false;

                if (File.Exists(fullPath)) { File.Delete(fullPath); return true; }
                return false;
            }
            catch { return false; }
        }

        private static async Task<bool> ExecuteSystemCheckAsync(CancellationToken ct)
        {
            try
            {
                var health = new StartupHealthService();
                var result = await health.CheckAndRepairAsync().ConfigureAwait(false);
                return result.IsHealthy;
            }
            catch { return false; }
        }

        private static readonly string[] _blockedPsPatterns =
        [
            "format-volume",
            "clear-disk",
            "initialize-disk",
            "set-partition",
            "remove-item -recurse",
            "remove-item -r ",
            "ri -recurse",
            "invoke-expression",
            "iex ",
            "iex(",
            "-encodedcommand",
            "-enc ",
            "[convert]::frombase64string",
            "::frombase64string",
            "downloadstring",
            "downloadfile",
            "invoke-webrequest",
            "iwr ",
            "new-object net.webclient",
            "[net.webclient]",
            "[system.reflection.assembly]::load",
            "[reflection.assembly]::load",
            "assembly::loadfrom",
            "assembly::loadfile",
            "set-mppreference",
            "add-mppreference",
            "disable-windowsoptionalfeature",
            "net user",
            "net localgroup",
            "add-localgroup",
            "new-localuser",
            "remove-itemproperty",
            "set-itemproperty.*hklm",
        ];

        private static async Task<bool> ExecuteRunPowerShellAsync(FlowItem item, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(item.PowerShellScript)) return false;

            // Normalise : minuscules, espaces multiples → un seul, supprime les backticks PS (char d'échappement)
            string normalizedScript = NormalizeForBlockCheck(item.PowerShellScript.Replace("`", ""));

            foreach (var bp in _blockedPsPatterns)
            {
                bool matched = bp.Contains('*')
                    ? System.Text.RegularExpressions.Regex.IsMatch(normalizedScript,
                        System.Text.RegularExpressions.Regex.Escape(bp).Replace(@"\*", ".*"))
                    : normalizedScript.Contains(bp);

                if (matched)
                {
                    LoggingService.LogError($"Blocked dangerous PowerShell [{bp}]: {item.PowerShellScript[..Math.Min(80, item.PowerShellScript.Length)]}");
                    return false;
                }
            }

            string tempFile = Path.Combine(Path.GetTempPath(), $"coclico_ps_{Guid.NewGuid():N}.ps1");
            try
            {
                await File.WriteAllTextAsync(tempFile, item.PowerShellScript, Encoding.UTF8, ct);
                using var ps = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NonInteractive -ExecutionPolicy Bypass -File \"{tempFile}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                ps.Start();
                if (item.WaitForPreviousExit) await ps.WaitForExitAsync(ct);
                return true;
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        private static bool ExecuteOpenUrl(FlowItem item)
        {
            if (string.IsNullOrWhiteSpace(item.UrlToOpen)) return false;
            if (!Uri.TryCreate(item.UrlToOpen, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
                return true;
            }
            catch { return false; }
        }

        [DllImport("winmm.dll")] private static extern int waveOutSetVolume(IntPtr hwo, uint dwVolume);

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class _MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr ppDevices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate([MarshalAs(UnmanagedType.LPStruct)] Guid riid, int dwClsCtx,
                IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
            [PreserveSig] int GetChannelCount(out int pnChannelCount);
            [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, IntPtr pguidEventContext);
            [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, IntPtr pguidEventContext);
        }

        private static bool ExecuteSetVolume(FlowItem item)
        {
            try
            {
                float scalar = Math.Clamp(item.VolumeLevel, 0, 100) / 100f;

                var enumerator = (IMMDeviceEnumerator)new _MMDeviceEnumeratorCom();
                enumerator.GetDefaultAudioEndpoint(0, 1, out var device);
                device.Activate(new Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), 23, IntPtr.Zero, out var obj);
                var endpointVol = (IAudioEndpointVolume)obj;
                endpointVol.SetMasterVolumeLevelScalar(scalar, IntPtr.Zero);

                Marshal.FinalReleaseComObject(endpointVol);
                Marshal.FinalReleaseComObject(device);
                Marshal.FinalReleaseComObject(enumerator);
                return true;
            }
            catch
            {
                try
                {
                    uint wv = (uint)(Math.Clamp(item.VolumeLevel, 0, 100) * 0xFFFF / 100);
                    waveOutSetVolume(IntPtr.Zero, wv | (wv << 16));
                    return true;
                }
                catch { return false; }
            }
        }

        [DllImport("user32.dll")] private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        private const uint WM_APPCOMMAND = 0x0319;
        private const int APPCOMMAND_VOLUME_MUTE = 8;

        private static bool ExecuteMuteAudio()
        {
            try
            {
                var hWnd = FindWindow("Shell_TrayWnd", null);
                SendMessage(hWnd, WM_APPCOMMAND, IntPtr.Zero, (IntPtr)(APPCOMMAND_VOLUME_MUTE * 0x10000));
                return true;
            }
            catch { return false; }
        }

        private static bool ExecuteSetProcessPriority(FlowItem item)
        {
            if (string.IsNullOrWhiteSpace(item.ProcessNamePriority)) return false;
            try
            {
                string procName = item.ProcessNamePriority;
                if (procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    procName = procName[..^4];

                var priorityClass = (item.PriorityLevel ?? "Normal") switch
                {
                    "Idle"         => ProcessPriorityClass.Idle,
                    "BelowNormal"  => ProcessPriorityClass.BelowNormal,
                    "AboveNormal"  => ProcessPriorityClass.AboveNormal,
                    "High"         => ProcessPriorityClass.High,
                    "RealTime"     => ProcessPriorityClass.RealTime,
                    _              => ProcessPriorityClass.Normal
                };
                bool changed = false;
                foreach (var p in Process.GetProcessesByName(procName))
                {
                    using (p) { try { p.PriorityClass = priorityClass; changed = true; } catch { } }
                }
                return changed;
            }
            catch { return false; }
        }

        private static bool ExecuteKillByMemory()
        {
            try
            {
                var victim = Process.GetProcesses()
                    .Where(p => { try { return !p.HasExited && p.WorkingSet64 > 0; } catch { return false; } })
                    .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0; } })
                    .FirstOrDefault(p =>
                    {
                        string name = p.ProcessName.ToLowerInvariant();
                        return name != "system" && name != "idle" && name != "coclico" && name != "explorer";
                    });
                if (victim == null) return false;
                using (victim) { victim.Kill(); return true; }
            }
            catch { return false; }
        }

        private static async Task<bool> ExecuteCleanTempAsync(CancellationToken ct)
        {
            try
            {
                string temp = Path.GetTempPath();
                long freed = 0;
                foreach (string file in Directory.EnumerateFiles(temp, "*", SearchOption.TopDirectoryOnly))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var fi = new FileInfo(file);
                        freed += fi.Length;
                        fi.Delete();
                    }
                    catch { }
                }
                await Task.CompletedTask;
                LoggingService.LogInfo($"CleanTemp: freed ~{freed / 1024 / 1024} MB");
                return true;
            }
            catch { return false; }
        }

        private static bool ExecuteRamClean()
        {
            try
            {
                MemoryCleanerService.EmptyWorkingSets();
                MemoryCleanerService.FlushStandbyList();
                return true;
            }
            catch { return false; }
        }

        private static bool ExecuteClipboardSet(FlowItem item)
        {
            if (item.ClipboardText == null) return false;
            try
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    System.Windows.Clipboard.SetText(item.ClipboardText));
                return true;
            }
            catch { return false; }
        }

        private static async Task<bool> ExecuteScreenshotAsync(FlowItem item, CancellationToken ct)
        {
            try
            {
                string folder = string.IsNullOrWhiteSpace(item.ScreenshotPath)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
                    : item.ScreenshotPath!;
                Directory.CreateDirectory(folder);
                string file   = Path.Combine(folder, $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                string tmpPs1 = Path.Combine(Path.GetTempPath(), $"coclico_ss_{Guid.NewGuid():N}.ps1");

                string psScript =
                    $"Add-Type -AssemblyName System.Drawing\r\n" +
                    $"Add-Type -AssemblyName System.Windows.Forms\r\n" +
                    $"$s=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds\r\n" +
                    $"$b=New-Object System.Drawing.Bitmap($s.Width,$s.Height)\r\n" +
                    $"$g=[System.Drawing.Graphics]::FromImage($b)\r\n" +
                    $"$g.CopyFromScreen($s.Location,[System.Drawing.Point]::Empty,$s.Size)\r\n" +
                    $"$b.Save('{file.Replace("'", "''")}')\r\n" +
                    $"$g.Dispose(); $b.Dispose()";

                await File.WriteAllTextAsync(tmpPs1, psScript, System.Text.Encoding.UTF8, ct);

                await Task.Run(() =>
                {
                    using var ps = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName  = "powershell.exe",
                            Arguments = $"-NonInteractive -ExecutionPolicy Bypass -File \"{tmpPs1}\"",
                            UseShellExecute  = false,
                            CreateNoWindow   = true
                        }
                    };
                    ps.Start();
                    ps.WaitForExit(12000);
                }, ct);

                try { File.Delete(tmpPs1); } catch { }
                return File.Exists(file);
            }
            catch { return false; }
        }

        private static bool ExecuteSendKeys(FlowItem item)
        {
            if (string.IsNullOrEmpty(item.SendKeysText)) return false;
            try
            {
                using var ps = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NonInteractive -ExecutionPolicy Bypass -Command " +
                                    $"\"Add-Type -AssemblyName System.Windows.Forms; " +
                                    $"[System.Windows.Forms.SendKeys]::SendWait('{item.SendKeysText!.Replace("'", "''")}')\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                ps.Start();
                ps.WaitForExit(5000);
                return true;
            }
            catch { return false; }
        }

        private static async Task<bool> ExecuteCompressFileAsync(FlowItem item, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(item.CompressSource) || string.IsNullOrWhiteSpace(item.CompressDest))
                return false;
            try
            {
                await Task.Run(() =>
                {
                    if (File.Exists(item.CompressSource))
                    {
                        string? dir = Path.GetDirectoryName(item.CompressDest);
                        if (dir != null) Directory.CreateDirectory(dir);
                        using var zip = ZipFile.Open(item.CompressDest!, ZipArchiveMode.Create);
                        zip.CreateEntryFromFile(item.CompressSource!, Path.GetFileName(item.CompressSource!));
                    }
                    else if (Directory.Exists(item.CompressSource))
                    {
                        ZipFile.CreateFromDirectory(item.CompressSource!, item.CompressDest!);
                    }
                }, ct);
                return true;
            }
            catch { return false; }
        }

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        private static bool ExecuteEmptyRecycleBin()
        {
            try { SHEmptyRecycleBin(IntPtr.Zero, null, 0x0001 | 0x0002); return true; }
            catch { return false; }
        }

        [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] private static extern short VkKeyScan(char ch);
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP       = 0x0002;

        private static bool ExecuteTriggerShortcut(FlowItem item)
        {
            if (string.IsNullOrWhiteSpace(item.TriggerShortcutKeys)) return false;
            try
            {
                var parts   = item.TriggerShortcutKeys!.Split('+');
                var vkCodes = parts.Select(ParseVirtualKey).Where(v => v != 0).ToArray();
                if (vkCodes.Length == 0) return false;

                foreach (var vk in vkCodes)
                    keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);

                Thread.Sleep(50);

                foreach (var vk in vkCodes.Reverse())
                    keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);

                return true;
            }
            catch { return false; }
        }

        private static byte ParseVirtualKey(string key) => key.Trim().ToLowerInvariant() switch
        {
            "ctrl" or "control"    => 0x11,
            "alt"                  => 0x12,
            "shift"                => 0x10,
            "win" or "winkey"      => 0x5B,
            "f1"  => 0x70, "f2"  => 0x71, "f3"  => 0x72,  "f4"  => 0x73,
            "f5"  => 0x74, "f6"  => 0x75, "f7"  => 0x76,  "f8"  => 0x77,
            "f9"  => 0x78, "f10" => 0x79, "f11" => 0x7A,  "f12" => 0x7B,
            "tab"      => 0x09, "enter"    => 0x0D, "esc" or "escape" => 0x1B,
            "space"    => 0x20, "left"     => 0x25, "up"             => 0x26,
            "right"    => 0x27, "down"     => 0x28, "home"           => 0x24,
            "end"      => 0x23, "pageup"   => 0x21, "pagedown"       => 0x22,
            "del" or "delete" => 0x2E, "backspace" => 0x08,
            _ => key.Trim().Length == 1 ? ParseSingleCharVk(key.Trim()[0]) : (byte)0
        };

        private static byte ParseSingleCharVk(char ch)
        {
            if (char.IsAsciiLetterOrDigit(ch)) return (byte)char.ToUpper(ch);
            short vk = VkKeyScan(ch);
            return vk >= 0 ? (byte)(vk & 0xFF) : (byte)0;
        }

    }
}
