using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Coclico.Modules.Cleaning.Models;
using Coclico.Services;

namespace Coclico.Modules.Cleaning.Services;

public class CleaningService
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("dnsapi.dll")]
    private static extern bool DnsFlushResolverCache();

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    private static void QueryRecycleBin(string? rootPath, out long totalBytes, out long totalItems)
    {
        totalBytes = 0;
        totalItems = 0;
        try
        {
            var info = new SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<SHQUERYRBINFO>() };
            if (SHQueryRecycleBin(rootPath, ref info) == 0)
            {
                totalBytes = info.i64Size;
                totalItems = info.i64NumItems;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.InvalidOperationException) { LoggingService.LogException(ex, "CleaningService.QueryRecycleBin"); }
    }

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    private static readonly Dictionary<string, Func<CleaningCategory, CancellationToken, Task>> CleanActions = new()
    {
        ["TempFiles"] = CleanWindowsTempAsync,
        ["RecycleBin"] = CleanRecycleBinAsync,
        ["Thumbnails"] = CleanThumbnailCacheAsync,
        ["DnsCache"] = FlushDnsCacheAsync,
        ["BrowserCache"] = CleanBrowserCachesAsync,
        ["SystemLogs"] = CleanSystemLogsAsync,
        ["WindowsUpdate"] = CleanWindowsUpdateDownloadAsync,
        ["GpuShaders"] = CleanGpuShadersAsync,
        ["MemoryDumps"] = CleanCrashDumpsAsync,
        ["DeliveryOptimization"] = CleanDeliveryOptimizationAsync,
        ["WinSxS"] = CleanWinSxSComponentStoreAsync
    };

    private static readonly HashSet<string> ProtectedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sqlite", ".sqlite3", ".db", ".db-wal", ".db-shm", ".bak"
    };

    private static readonly HashSet<string> ProtectedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bookmarks", "preferences", "cookies", "login data", "web data",
        "history", "favicons", "secure preferences", "places.sqlite", "key4.db", "cert9.db", "logins.json"
    };

    public static string FormatSize(long bytes)
    {
        bool isEn = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase);
        string mbUnit = isEn ? "MB" : "Mo";
        string kbUnit = isEn ? "KB" : "Ko";
        string gbUnit = isEn ? "GB" : "Go";

        return bytes <= 0
            ? $"0 {mbUnit}"
            : bytes < 1024 * 1024
            ? $"{bytes / 1024.0:F0} {kbUnit}"
            : bytes < 1024L * 1024 * 1024 ? $"{bytes / (1024.0 * 1024):F1} {mbUnit}" : $"{bytes / (1024.0 * 1024 * 1024):F2} {gbUnit}";
    }
    private static string GetWindowsDrive()
    {
        try
        {
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string? root = Path.GetPathRoot(winDir);
            if (!string.IsNullOrEmpty(root))
            {
                return root[0].ToString().ToUpperInvariant();
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { LoggingService.LogException(ex, "CleaningService.GetWindowsDrive"); }
        return "C";
    }

    public List<CleaningCategory> GetAvailableCategories()
    {
        return
        [
            new()
            {
                Key = "TempFiles",
                Name = "Fichiers temporaires Windows",
                Icon = "🪟",
                Description = "Résidus d'applications et fichiers temporaires du système",
                TargetPath = @"C:\Windows\Temp et %TEMP%",
                PresetLevel = 1,
                IsSelected = true
            },
            new()
            {
                Key = "RecycleBin",
                Name = "Corbeille Windows",
                Icon = "🗑️",
                Description = "Fichiers et dossiers supprimés stockés sur tous les disques",
                TargetPath = @"$Recycle.Bin",
                PresetLevel = 1,
                IsSelected = true
            },
            new()
            {
                Key = "Thumbnails",
                Name = "Cache des miniatures",
                Icon = "🖼️",
                Description = "Aperçus et bases de données miniatures de l'Explorateur",
                TargetPath = @"AppData\Local\Microsoft\Windows\Explorer",
                PresetLevel = 1,
                IsSelected = true
            },
            new()
            {
                Key = "DnsCache",
                Name = "Cache DNS & Réseau",
                Icon = "🌍",
                Description = "Adresses IP en cache et résolution DNS obsolète",
                TargetPath = @"Mémoire réseau Windows",
                PresetLevel = 1,
                IsSelected = true
            },
            new()
            {
                Key = "BrowserCache",
                Name = "Cache des navigateurs",
                Icon = "🌐",
                Description = "Fichiers Web, images et scripts en cache (Chrome, Edge, Firefox)",
                TargetPath = @"AppData\Local\(Navigateurs)\Cache",
                PresetLevel = 2,
                IsSelected = true
            },
            new()
            {
                Key = "SystemLogs",
                Name = "Logs système & rapports d'erreur",
                Icon = "📋",
                Description = "Fichiers journaux d'événements et rapports de plantage WER",
                TargetPath = @"C:\Windows\Logs & WER",
                PresetLevel = 2,
                IsSelected = true
            },
            new()
            {
                Key = "WindowsUpdate",
                Name = "Téléchargements Windows Update",
                Icon = "📦",
                Description = "Paquets de mise à jour déjà installés et résidus de téléchargement",
                TargetPath = @"C:\Windows\SoftwareDistribution\Download",
                PresetLevel = 2,
                IsSelected = true
            },
            new()
            {
                Key = "GpuShaders",
                Name = "Cache des Shaders GPU (DirectX / NVIDIA / AMD)",
                Icon = "🎮",
                Description = "Fichiers de compilation graphique shaders des jeux et logiciels 3D",
                TargetPath = @"AppData\Local\D3DSCache, NVIDIA\DXCache",
                PresetLevel = 3,
                IsSelected = true
            },
            new()
            {
                Key = "MemoryDumps",
                Name = "Dumps mémoire & plantages système",
                Icon = "⚠️",
                Description = "Images mémoire (MEMORY.DMP et minidumps) suite à des écrans bleus",
                TargetPath = @"C:\Windows\Minidump & MEMORY.DMP",
                PresetLevel = 3,
                IsSelected = true
            },
            new()
            {
                Key = "DeliveryOptimization",
                Name = "Optimisation de livraison (WaaS)",
                Icon = "⚡",
                Description = "Fichiers de partage pair-à-pair des mises à jour Windows",
                TargetPath = @"DeliveryOptimization\Cache",
                PresetLevel = 3,
                IsSelected = true
            },
            new()
            {
                Key = "WinSxS",
                Name = "Nettoyage Magasin WinSxS (DISM)",
                Icon = "🛠️",
                Description = "Purge des anciennes versions de composants et Service Packs",
                TargetPath = @"C:\Windows\WinSxS",
                PresetLevel = 3,
                IsSelected = true
            }
        ];
    }

    public async Task ScanAllCategoriesAsync(IEnumerable<CleaningCategory> categories, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            DateTime cleanupCutoff = DateTime.UtcNow.AddHours(-1);
            DateTime logsCutoff = DateTime.UtcNow.AddDays(-7);

            foreach (CleaningCategory cat in categories)
            {
                ct.ThrowIfCancellationRequested();
                long bytes = 0;
                int count = 0;
                bool measurable = true;

                try
                {
                    string catIdentifier = !string.IsNullOrEmpty(cat.Key) ? cat.Key : cat.Name;
                    switch (catIdentifier)
                    {
                        case "TempFiles":
                        case "Fichiers temporaires Windows":
                            ScanDirectory(Path.GetTempPath(), cleanupCutoff, ref bytes, ref count);
                            ScanDirectory(Path.Join(windows, "Temp"), cleanupCutoff, ref bytes, ref count);
                            break;

                        case "RecycleBin":
                        case "Corbeille Windows":
                            foreach (DriveInfo drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                            {
                                QueryRecycleBin(drive.RootDirectory.FullName, out long rbBytes, out long rbItems);
                                bytes += rbBytes;
                                count += (int)rbItems;
                            }
                            break;

                        case "Thumbnails":
                        case "Cache des miniatures":
                            string thumbDir = Path.Join(local, @"Microsoft\Windows\Explorer");
                            if (Directory.Exists(thumbDir))
                            {
                                foreach (string f in Directory.EnumerateFiles(thumbDir, "thumbcache_*.db"))
                                {
                                    try { var fi = new FileInfo(f); bytes += fi.Length; count++; } catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "CleaningService.ScanAllCategories"); }
                                }
                            }
                            break;

                        case "DnsCache":
                        case "Cache DNS & Réseau":
                            measurable = false;
                            break;

                        case "BrowserCache":
                        case "Cache des navigateurs":
                            foreach (string browserDir in GetBrowserCachePaths(local))
                            {
                                ScanDirectory(browserDir, cleanupCutoff, ref bytes, ref count);
                            }

                            string firefoxProfilesDir = Path.Join(local, @"Mozilla\Firefox\Profiles");
                            if (Directory.Exists(firefoxProfilesDir))
                            {
                                foreach (string profile in Directory.EnumerateDirectories(firefoxProfilesDir))
                                {
                                    ScanDirectory(Path.Join(profile, "cache2"), cleanupCutoff, ref bytes, ref count);
                                }
                            }
                            break;

                        case "SystemLogs":
                        case "Logs système & rapports d'erreur":
                            ScanDirectory(Path.Join(windows, "Logs"), logsCutoff, ref bytes, ref count);
                            ScanDirectory(Path.Join(windows, @"System32\LogFiles"), logsCutoff, ref bytes, ref count);
                            ScanDirectory(Path.Join(appData, @"Microsoft\Windows\WER"), logsCutoff, ref bytes, ref count);
                            ScanDirectory(Path.Join(local, @"Microsoft\Windows\WER"), logsCutoff, ref bytes, ref count);
                            break;

                        case "WindowsUpdate":
                        case "Téléchargements Windows Update":
                            ScanDirectory(Path.Join(windows, @"SoftwareDistribution\Download"), cleanupCutoff, ref bytes, ref count);
                            break;

                        case "GpuShaders":
                        case "Cache des Shaders GPU (DirectX / NVIDIA / AMD)":
                            string[] shaderDirs =
                            [
                                Path.Join(local, "D3DSCache"),
                                Path.Join(local, @"NVIDIA\DXCache"),
                                Path.Join(local, @"NVIDIA\GLCache"),
                                Path.Join(local, @"AMD\DxcCache")
                            ];
                            foreach (string shaderDir in shaderDirs)
                            {
                                ScanDirectory(shaderDir, cleanupCutoff, ref bytes, ref count);
                            }

                            break;

                        case "MemoryDumps":
                        case "Dumps mémoire & plantages système":
                            ScanDirectory(Path.Join(windows, "Minidump"), cleanupCutoff, ref bytes, ref count);
                            string memDmp = Path.Join(windows, "MEMORY.DMP");
                            if (File.Exists(memDmp))
                            {
                                try { var fi = new FileInfo(memDmp); bytes += fi.Length; count++; } catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "CleaningService.ScanAllCategories"); }
                            }
                            break;

                        case "DeliveryOptimization":
                        case "Optimisation de livraison (WaaS)":
                            string waasDir = Path.Join(windows, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization");
                            ScanDirectory(waasDir, cleanupCutoff, ref bytes, ref count);
                            break;

                        case "WinSxS":
                        case "Nettoyage Magasin WinSxS (DISM)":
                            measurable = false;
                            break;
                    }
                }
                catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { LoggingService.LogException(ex, "CleaningService.ScanAllCategories"); }

                cat.BytesFound = bytes;
                cat.FileCount = count;
                cat.IsSizeMeasurable = measurable;
            }
        }, ct);
    }

    public async Task<CleaningResult> ExecuteDeepCleanAsync(
        List<CleaningCategory> selectedCategories,
        IProgress<(string status, int percent, long bytesFreed)>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new CleaningResult();
        int totalSteps = selectedCategories.Count(c => c.IsSelected);
        if (totalSteps == 0)
        {
            return result;
        }

        int currentStep = 0;

        foreach (CleaningCategory category in selectedCategories.Where(c => c.IsSelected))
        {
            ct.ThrowIfCancellationRequested();
            currentStep++;
            int percent = (int)((double)currentStep / totalSteps * 100);
            progress?.Report(($"Nettoyage : {category.Name}...", percent, result.TotalBytesFreed));

            try
            {
                if (!string.IsNullOrEmpty(category.Key) &&
                    CleanActions.TryGetValue(category.Key, out var action))
                {
                    await action(category, ct);
                }
                else
                {
                    LoggingService.LogWarning($"[DeepCleaning] Aucune action de nettoyage pour la clé '{category.Key}' (catégorie '{category.Name}') - ignorée.");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not System.OutOfMemoryException)
            {
                result.Errors.Add($"{category.Name}: {ex.Message}");
                LoggingService.LogException(ex, $"DeepCleaning.{category.Name}");
            }

            result.TotalBytesFreed += category.BytesFreed;
            result.FilesDeleted += category.FilesDeleted;
            result.Categories.Add(category);
        }

        sw.Stop();
        result.ElapsedTime = sw.Elapsed;
        result.DirectoriesCleaned = selectedCategories.Count(c => c.IsSelected);

        return result;
    }
    private static async Task CleanWindowsTempAsync(CleaningCategory cat, CancellationToken ct)
    {
        string winTemp = Path.GetTempPath();
        string systemTemp = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        await CleanDirectorySafeAsync(winTemp, cat, ct);
        await CleanDirectorySafeAsync(systemTemp, cat, ct);
    }

    private static async Task CleanRecycleBinAsync(CleaningCategory cat, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            try
            {
                QueryRecycleBin(null, out long rbBytes, out long rbItems);
                _ = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                cat.BytesFreed += rbBytes;
                cat.FilesDeleted += (int)rbItems;
            }
            catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "DeepCleaning.RecycleBin"); }
        }, ct);
    }

    private static async Task CleanThumbnailCacheAsync(CleaningCategory cat, CancellationToken ct)
    {
        string thumbCache = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\Windows\Explorer");

        await Task.Run(() =>
        {
            if (!Directory.Exists(thumbCache))
            {
                return;
            }

            try
            {
                foreach (string file in Directory.EnumerateFiles(thumbCache, "thumbcache_*.db"))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var fi = new FileInfo(file);
                        cat.BytesFreed += fi.Length;
                        fi.Delete();
                        cat.FilesDeleted++;
                    }
                    catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "CleaningService.CleanThumbnailCache"); }
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.OperationCanceledException) { LoggingService.LogException(ex, "DeepCleaning.ThumbnailCache"); }
        }, ct);
    }

    private static async Task FlushDnsCacheAsync(CleaningCategory cat, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            try
            {
                _ = DnsFlushResolverCache();
                cat.FilesDeleted += 1;
            }
            catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "DeepCleaning.FlushDns"); }
        }, ct);
    }

    private static async Task CleanBrowserCachesAsync(CleaningCategory cat, CancellationToken ct)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        foreach (string path in GetBrowserCachePaths(localAppData))
        {
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(path))
            {
                await CleanDirectorySafeAsync(path, cat, ct);
            }
        }

        string firefoxProfilesDir = Path.Join(localAppData, @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(firefoxProfilesDir))
        {
            foreach (string cache2 in Directory.EnumerateDirectories(firefoxProfilesDir).Select(profile => Path.Join(profile, "cache2")).Where(Directory.Exists))
            {
                await CleanDirectorySafeAsync(cache2, cat, ct);
            }
        }
    }

    private static async Task CleanSystemLogsAsync(CleaningCategory cat, CancellationToken ct)
    {
        string[] logPaths =
        [
            Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs"),
            Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\LogFiles"),
            Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\WER"),
            Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\WER"),
        ];

        foreach (string path in logPaths)
        {
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(path))
            {
                await CleanOldFilesAsync(path, TimeSpan.FromDays(7), cat, ct);
            }
        }
    }

    private static async Task CleanWindowsUpdateDownloadAsync(CleaningCategory cat, CancellationToken ct)
    {
        string softwareDist = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"SoftwareDistribution\Download");

        if (Directory.Exists(softwareDist))
        {
            await CleanDirectorySafeAsync(softwareDist, cat, ct);
        }
    }

    private static async Task CleanGpuShadersAsync(CleaningCategory cat, CancellationToken ct)
    {
        foreach (string dir in MemoryCleanerService.GetGpuShaderCacheDirectories())
        {
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(dir))
            {
                await CleanDirectorySafeAsync(dir, cat, ct);
            }
        }
    }

    private static async Task CleanCrashDumpsAsync(CleaningCategory cat, CancellationToken ct)
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string miniDump = Path.Join(windows, "Minidump");
        if (Directory.Exists(miniDump))
        {
            await CleanDirectorySafeAsync(miniDump, cat, ct);
        }

        string memDmp = Path.Join(windows, "MEMORY.DMP");
        if (File.Exists(memDmp))
        {
            try
            {
                var fi = new FileInfo(memDmp);
                cat.BytesFreed += fi.Length;
                File.Delete(memDmp);
                cat.FilesDeleted++;
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { LoggingService.LogException(ex, "CleaningService.CleanCrashDumps"); }
        }
    }

    private static async Task CleanDeliveryOptimizationAsync(CleaningCategory cat, CancellationToken ct)
    {
        string waasDir = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization");

        if (Directory.Exists(waasDir))
        {
            await CleanDirectorySafeAsync(waasDir, cat, ct);
        }
    }

    private static async Task CleanWinSxSComponentStoreAsync(CleaningCategory cat, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                try
                {
                    await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "CleaningService.CleanWinSxS"); }
                    throw;
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.InvalidOperationException) { LoggingService.LogException(ex, "DeepCleaning.WinSxS"); }
    }

    private static bool IsProtectedFile(FileInfo fi)
    {
        if (ProtectedExtensions.Contains(fi.Extension))
        {
            return true;
        }

        string name = Path.GetFileNameWithoutExtension(fi.Name);
        return ProtectedFileNames.Contains(name) || ProtectedFileNames.Contains(fi.Name);
    }

    private static DirectoryInfo? GetSafeRoot(string path, out string rootPath)
    {
        rootPath = string.Empty;
        if (!Directory.Exists(path))
        {
            return null;
        }

        rootPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootInfo = new DirectoryInfo(rootPath);
        return rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint) ? null : rootInfo;
    }

    private static void PushSafeSubdirectories(DirectoryInfo current, string rootPath, Stack<DirectoryInfo> dirStack)
    {
        try
        {
            foreach (DirectoryInfo sub in current.EnumerateDirectories())
            {
                if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                string subPath = Path.GetFullPath(sub.FullName);
                if (subPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    dirStack.Push(sub);
                }
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { LoggingService.LogException(ex, "CleaningService.PushSafeSubdirectories"); }
    }

    private static bool IsDeletableFile(FileInfo fi, string rootPath, DateTime cutoff)
    {
        string filePath = Path.GetFullPath(fi.FullName);
        if (!filePath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return false;
        }

        if (IsProtectedFile(fi))
        {
            return false;
        }

        return fi.LastWriteTimeUtc <= cutoff;
    }

    private static string[] GetBrowserCachePaths(string localAppData)
    {
        return
        [
            Path.Join(localAppData, @"Google\Chrome\User Data\Default\Cache"),
            Path.Join(localAppData, @"Google\Chrome\User Data\Default\Code Cache"),
            Path.Join(localAppData, @"Microsoft\Edge\User Data\Default\Cache"),
            Path.Join(localAppData, @"Microsoft\Edge\User Data\Default\Code Cache"),
            Path.Join(localAppData, @"BraveSoftware\Brave-Browser\User Data\Default\Cache"),
            Path.Join(localAppData, @"Opera Software\Opera Stable\Cache"),
        ];
    }

    private static void ScanDirectory(string path, DateTime cutoff, ref long bytes, ref int count)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            DirectoryInfo? rootInfo = GetSafeRoot(path, out string rootPath);
            if (rootInfo == null)
            {
                return;
            }

            var dirStack = new Stack<DirectoryInfo>();
            dirStack.Push(rootInfo);

            while (dirStack.Count > 0)
            {
                DirectoryInfo current = dirStack.Pop();
                PushSafeSubdirectories(current, rootPath, dirStack);

                try
                {
                    foreach (FileInfo fi in current.EnumerateFiles().Where(fi => IsDeletableFile(fi, rootPath, cutoff)))
                    {
                        bytes += fi.Length;
                        count++;
                    }
                }
                catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "CleaningService.ScanDirectory"); }
            }
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "CleaningService.ScanDirectory"); }
    }

    private static async Task CleanDirectorySafeAsync(string path, CleaningCategory cat, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            try
            {
                DirectoryInfo? rootInfo = GetSafeRoot(path, out string rootPath);
                if (rootInfo == null)
                {
                    return;
                }

                DateTime cutoff = DateTime.UtcNow.AddHours(-1);
                var dirStack = new Stack<DirectoryInfo>();
                var emptyDirCandidates = new List<string>();

                dirStack.Push(rootInfo);

                while (dirStack.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    DirectoryInfo current = dirStack.Pop();
                    PushSafeSubdirectories(current, rootPath, dirStack);

                    try
                    {
                        foreach (FileInfo fi in current.EnumerateFiles())
                        {
                            ct.ThrowIfCancellationRequested();
                            try
                            {
                                if (IsDeletableFile(fi, rootPath, cutoff))
                                {
                                    cat.BytesFreed += fi.Length;
                                    fi.Delete();
                                    cat.FilesDeleted++;
                                }
                            }
                            catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "CleaningService.CleanDirectorySafe"); }
                        }
                    }
                    catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "CleaningService.CleanDirectorySafe"); }

                    if (!string.Equals(current.FullName, path, StringComparison.OrdinalIgnoreCase))
                    {
                        emptyDirCandidates.Add(current.FullName);
                    }
                }

                emptyDirCandidates.Sort((a, b) => b.Length.CompareTo(a.Length));
                foreach (string dir in emptyDirCandidates)
                {
                    try
                    {
                        if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                        {
                            Directory.Delete(dir);
                        }
                    }
                    catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { LoggingService.LogException(ex, "CleaningService.CleanDirectorySafe"); }
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.OperationCanceledException)
            {
                LoggingService.LogException(ex, $"DeepCleaning.CleanDir({path})");
            }
        }, ct);
    }

    private static async Task CleanOldFilesAsync(string path, TimeSpan maxAge, CleaningCategory cat, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            DateTime cutoff = DateTime.UtcNow - maxAge;
            try
            {
                DirectoryInfo? rootInfo = GetSafeRoot(path, out string rootPath);
                if (rootInfo == null)
                {
                    return;
                }

                var dirStack = new Stack<DirectoryInfo>();
                dirStack.Push(rootInfo);

                while (dirStack.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    DirectoryInfo current = dirStack.Pop();
                    PushSafeSubdirectories(current, rootPath, dirStack);

                    try
                    {
                        foreach (FileInfo fi in current.EnumerateFiles())
                        {
                            ct.ThrowIfCancellationRequested();
                            try
                            {
                                if (IsDeletableFile(fi, rootPath, cutoff))
                                {
                                    cat.BytesFreed += fi.Length;
                                    fi.Delete();
                                    cat.FilesDeleted++;
                                }
                            }
                            catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "CleaningService.CleanOldFiles"); }
                        }
                    }
                    catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "CleaningService.CleanOldFiles"); }
                }
            }
            catch (Exception ex) when (ex is not System.OutOfMemoryException)
            {
                LoggingService.LogException(ex, $"DeepCleaning.CleanOldFiles({path})");
            }
        }, ct);
    }
}
