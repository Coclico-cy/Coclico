using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Coclico.Services;

public class CleaningService
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    [DllImport("dnsapi.dll")]
    private static extern bool DnsFlushResolverCache();

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

    public sealed class CleaningResult
    {
        public long TotalBytesFreed { get; set; }
        public int FilesDeleted { get; set; }
        public int DirectoriesCleaned { get; set; }
        public List<CleaningCategory> Categories { get; set; } = [];
        public TimeSpan ElapsedTime { get; set; }
        public List<string> Errors { get; set; } = [];
    }

    public sealed class CleaningCategory : INotifyPropertyChanged
    {
        public string Key { get; set; } = string.Empty;

        public string Name
        {
            get
            {
                if (!string.IsNullOrEmpty(Key))
                {
                    string? loc = ServiceContainer.GetOptional<LocalizationService>()?.Get($"Cleaning_Cat_{Key}_Name");
                    if (!string.IsNullOrEmpty(loc) && !loc.StartsWith("Cleaning_Cat_"))
                    {
                        return loc;
                    }
                }
                return field;
            }
            set => field = value;
        } = string.Empty;

        public string Icon { get; set; } = "🗑️";

        public string Description
        {
            get
            {
                if (!string.IsNullOrEmpty(Key))
                {
                    string? loc = ServiceContainer.GetOptional<LocalizationService>()?.Get($"Cleaning_Cat_{Key}_Desc");
                    if (!string.IsNullOrEmpty(loc) && !loc.StartsWith("Cleaning_Cat_"))
                    {
                        return loc;
                    }
                }
                return field;
            }
            set => field = value;
        } = string.Empty;

        public string TargetPath { get; set; } = string.Empty;
        public int PresetLevel { get; set; } = 2;

        public long BytesFound
        {
            get;
            set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeText)); }
        }

        public string SizeText => FormatSize(BytesFound);

        public int FileCount
        {
            get;
            set { field = value; OnPropertyChanged(); }
        }

        public long BytesFreed { get; set; }
        public int FilesDeleted { get; set; }

        public bool IsSelected
        {
            get;
            set { field = value; OnPropertyChanged(); }
        } = true;

        public bool IsDeep => PresetLevel >= 3;

        public void NotifyLanguageChanged()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(SizeText));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public static string FormatSize(long bytes)
    {
        var loc = ServiceContainer.GetOptional<LocalizationService>();
        bool isEn = loc?.CurrentLanguage == "en";
        string mbUnit = isEn ? "MB" : "Mo";
        string kbUnit = isEn ? "KB" : "Ko";
        string gbUnit = isEn ? "GB" : "Go";

        return bytes <= 0
            ? $"0 {mbUnit}"
            : bytes < 1024 * 1024
            ? $"{bytes / 1024.0:F0} {kbUnit}"
            : bytes < 1024L * 1024 * 1024 ? $"{bytes / (1024.0 * 1024):F1} {mbUnit}" : $"{bytes / (1024.0 * 1024 * 1024):F2} {gbUnit}";
    }

    public async Task LaunchWindowsCleanupAsync()
    {
        string drive = GetWindowsDrive();
        await Task.Run(async () =>
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cleanmgr.exe",
                        Arguments = $"/d {drive}:",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Normal
                    }
                };

                _ = process.Start();
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception ex) { LoggingService.LogException(ex, "CleaningService.LaunchWindowsCleanup"); }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Impossible de lancer le nettoyage Windows : {ex.Message}", ex);
            }
        });
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
        catch (Exception ex) { LoggingService.LogException(ex, "CleaningService.GetWindowsDrive"); }
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

    public void ApplyPreset(IEnumerable<CleaningCategory> categories, string preset)
    {
        foreach (CleaningCategory cat in categories)
        {
            cat.IsSelected = preset switch
            {
                "Quick" => cat.PresetLevel == 1,
                "Recommended" => cat.PresetLevel <= 2,
                "Deep" => true,
                _ => cat.IsSelected
            };
        }
    }

    public async Task ScanAllCategoriesAsync(IEnumerable<CleaningCategory> categories, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            foreach (CleaningCategory cat in categories)
            {
                ct.ThrowIfCancellationRequested();
                long bytes = 0;
                int count = 0;

                try
                {
                    string catIdentifier = !string.IsNullOrEmpty(cat.Key) ? cat.Key : cat.Name;
                    switch (catIdentifier)
                    {
                        case "TempFiles":
                        case "Fichiers temporaires Windows":
                            ScanDirectory(Path.GetTempPath(), ref bytes, ref count);
                            ScanDirectory(Path.Combine(windows, "Temp"), ref bytes, ref count);
                            break;

                        case "RecycleBin":
                        case "Corbeille Windows":
                            foreach (DriveInfo drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                            {
                                string rPath = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
                                ScanDirectory(rPath, ref bytes, ref count);
                            }
                            break;

                        case "Thumbnails":
                        case "Cache des miniatures":
                            string thumbDir = Path.Combine(local, @"Microsoft\Windows\Explorer");
                            if (Directory.Exists(thumbDir))
                            {
                                foreach (string f in Directory.EnumerateFiles(thumbDir, "thumbcache_*.db"))
                                {
                                    try { var fi = new FileInfo(f); bytes += fi.Length; count++; } catch (Exception ex) { LoggingService.LogException(ex, "CleaningService.ScanAllCategories"); }
                                }
                            }
                            break;

                        case "DnsCache":
                        case "Cache DNS & Réseau":
                            bytes = 1024 * 1024;
                            count = 1;
                            break;

                        case "BrowserCache":
                        case "Cache des navigateurs":
                            foreach (string browserDir in GetBrowserCachePaths(local))
                            {
                                ScanDirectory(browserDir, ref bytes, ref count);
                            }

                            string firefoxProfilesDir = Path.Combine(local, @"Mozilla\Firefox\Profiles");
                            if (Directory.Exists(firefoxProfilesDir))
                            {
                                foreach (string profile in Directory.EnumerateDirectories(firefoxProfilesDir))
                                {
                                    ScanDirectory(Path.Combine(profile, "cache2"), ref bytes, ref count);
                                }
                            }
                            break;

                        case "SystemLogs":
                        case "Logs système & rapports d'erreur":
                            ScanDirectory(Path.Combine(windows, "Logs"), ref bytes, ref count);
                            ScanDirectory(Path.Combine(windows, @"System32\LogFiles"), ref bytes, ref count);
                            ScanDirectory(Path.Combine(appData, @"Microsoft\Windows\WER"), ref bytes, ref count);
                            ScanDirectory(Path.Combine(local, @"Microsoft\Windows\WER"), ref bytes, ref count);
                            break;

                        case "WindowsUpdate":
                        case "Téléchargements Windows Update":
                            ScanDirectory(Path.Combine(windows, @"SoftwareDistribution\Download"), ref bytes, ref count);
                            break;

                        case "GpuShaders":
                        case "Cache des Shaders GPU (DirectX / NVIDIA / AMD)":
                            string[] shaderDirs =
                            [
                                Path.Combine(local, "D3DSCache"),
                                Path.Combine(local, @"NVIDIA\DXCache"),
                                Path.Combine(local, @"NVIDIA\GLCache"),
                                Path.Combine(local, @"AMD\DxcCache")
                            ];
                            foreach (string shaderDir in shaderDirs)
                            {
                                ScanDirectory(shaderDir, ref bytes, ref count);
                            }

                            break;

                        case "MemoryDumps":
                        case "Dumps mémoire & plantages système":
                            ScanDirectory(Path.Combine(windows, "Minidump"), ref bytes, ref count);
                            string memDmp = Path.Combine(windows, "MEMORY.DMP");
                            if (File.Exists(memDmp))
                            {
                                try { var fi = new FileInfo(memDmp); bytes += fi.Length; count++; } catch (Exception ex) { LoggingService.LogException(ex, "CleaningService.ScanAllCategories"); }
                            }
                            break;

                        case "DeliveryOptimization":
                        case "Optimisation de livraison (WaaS)":
                            string waasDir = Path.Combine(windows, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization");
                            ScanDirectory(waasDir, ref bytes, ref count);
                            break;

                        case "WinSxS":
                        case "Nettoyage Magasin WinSxS (DISM)":
                            bytes = 500L * 1024 * 1024;
                            count = 1;
                            break;
                    }
                }
                catch (Exception ex) { LoggingService.LogException(ex, "CleaningService.ScanAllCategories"); }

                cat.BytesFound = bytes;
                cat.FileCount = count;
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
                    LoggingService.LogWarning($"[DeepCleaning] Aucune action de nettoyage pour la clé '{category.Key}' (catégorie '{category.Name}') — ignorée.");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
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

    public async Task<long> EstimateCleanableBytesAsync(CancellationToken ct = default)
    {
        List<CleaningCategory> cats = GetAvailableCategories();
        await ScanAllCategoriesAsync(cats, ct).ConfigureAwait(false);
        return cats.Sum(c => c.BytesFound);
    }

    private static async Task CleanWindowsTempAsync(CleaningCategory cat, CancellationToken ct)
    {
        string winTemp = Path.GetTempPath();
        string systemTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        await CleanDirectorySafeAsync(winTemp, cat, ct);
        await CleanDirectorySafeAsync(systemTemp, cat, ct);
    }

    private static async Task CleanRecycleBinAsync(CleaningCategory cat, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            try
            {
                _ = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                cat.FilesDeleted += Math.Max(1, cat.FileCount);
                cat.BytesFreed += cat.BytesFound;
            }
            catch (Exception ex) { LoggingService.LogException(ex, "DeepCleaning.RecycleBin"); }
        }, ct);
    }

    private static async Task CleanThumbnailCacheAsync(CleaningCategory cat, CancellationToken ct)
    {
        string thumbCache = Path.Combine(
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
                    catch (Exception ex) { LoggingService.LogException(ex, "CleaningService.CleanThumbnailCache"); }
                }
            }
            catch (Exception ex) { LoggingService.LogException(ex, "DeepCleaning.ThumbnailCache"); }
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
            catch (Exception ex) { LoggingService.LogException(ex, "DeepCleaning.FlushDns"); }
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

        string firefoxProfilesDir = Path.Combine(localAppData, @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(firefoxProfilesDir))
        {
            foreach (string profile in Directory.EnumerateDirectories(firefoxProfilesDir))
            {
                string cache2 = Path.Combine(profile, "cache2");
                if (Directory.Exists(cache2))
                {
                    await CleanDirectorySafeAsync(cache2, cat, ct);
                }
            }
        }
    }

    private static async Task CleanSystemLogsAsync(CleaningCategory cat, CancellationToken ct)
    {
        string[] logPaths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\LogFiles"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\WER"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\WER"),
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
        string softwareDist = Path.Combine(
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
        string miniDump = Path.Combine(windows, "Minidump");
        if (Directory.Exists(miniDump))
        {
            await CleanDirectorySafeAsync(miniDump, cat, ct);
        }

        string memDmp = Path.Combine(windows, "MEMORY.DMP");
        if (File.Exists(memDmp))
        {
            try
            {
                var fi = new FileInfo(memDmp);
                cat.BytesFreed += fi.Length;
                File.Delete(memDmp);
                cat.FilesDeleted++;
            }
            catch (Exception ex) { LoggingService.LogException(ex, "CleaningService.CleanCrashDumps"); }
        }
    }

    private static async Task CleanDeliveryOptimizationAsync(CleaningCategory cat, CancellationToken ct)
    {
        string waasDir = Path.Combine(
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
                    cat.BytesFreed += cat.BytesFound;
                    cat.FilesDeleted += 1;
                }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(entireProcessTree: true); } catch (Exception ex) { LoggingService.LogException(ex, "CleaningService.CleanWinSxS"); }
                    throw;
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { LoggingService.LogException(ex, "DeepCleaning.WinSxS"); }
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
        catch (Exception ex) { LoggingService.LogException(ex, "CleaningService.PushSafeSubdirectories"); }
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
            Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache"),
            Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Code Cache"),
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache"),
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Code Cache"),
            Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data\Default\Cache"),
            Path.Combine(localAppData, @"Opera Software\Opera Stable\Cache"),
        ];
    }

    private static void ScanDirectory(string path, ref long bytes, ref int count)
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
                    foreach (FileInfo fi in current.EnumerateFiles())
                    {
                        if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            continue;
                        }

                        bytes += fi.Length;
                        count++;
                    }
                }
                catch (Exception ex) { LoggingService.LogException(ex, "CleaningService.ScanDirectory"); }
            }
        }
        catch (Exception ex) { LoggingService.LogException(ex, "CleaningService.ScanDirectory"); }
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
                            catch (Exception ex) { LoggingService.LogException(ex, "CleaningService.CleanDirectorySafe"); }
                        }
                    }
                    catch (Exception ex) { LoggingService.LogException(ex, "CleaningService.CleanDirectorySafe"); }

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
                    catch (Exception ex) { LoggingService.LogException(ex, "CleaningService.CleanDirectorySafe"); }
                }
            }
            catch (Exception ex)
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
                            catch (Exception ex) { LoggingService.LogException(ex, "CleaningService.CleanOldFiles"); }
                        }
                    }
                    catch (Exception ex) { LoggingService.LogException(ex, "CleaningService.CleanOldFiles"); }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, $"DeepCleaning.CleanOldFiles({path})");
            }
        }, ct);
    }
}
