using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Coclico.Services
{
    public sealed class DeepCleaningService
    {
        public sealed class CleaningResult
        {
            public long TotalBytesFreed { get; set; }
            public int FilesDeleted { get; set; }
            public int DirectoriesCleaned { get; set; }
            public List<CleaningCategory> Categories { get; set; } = [];
            public TimeSpan ElapsedTime { get; set; }
            public List<string> Errors { get; set; } = [];
        }

        public sealed class CleaningCategory
        {
            public string Name { get; set; } = string.Empty;
            public string Icon { get; set; } = "🗑️";
            public long BytesFreed { get; set; }
            public int FilesDeleted { get; set; }
            public bool IsSelected { get; set; } = true;
        }

        public List<CleaningCategory> GetAvailableCategories()
        {
            return
            [
                new() { Name = "Fichiers temporaires Windows", Icon = "🪟" },
                new() { Name = "Cache navigateurs", Icon = "🌐" },
                new() { Name = "Logs système", Icon = "📋" },
                new() { Name = "Corbeille", Icon = "🗑️" },
                new() { Name = "Fichiers temporaires utilisateur", Icon = "👤" },
                new() { Name = "Cache miniatures", Icon = "🖼️" },
                new() { Name = "Rapports d'erreur Windows", Icon = "⚠️" },
                new() { Name = "Fichiers d'installation obsolètes", Icon = "📦" },
                new() { Name = "Cache DNS", Icon = "🌍" },
                new() { Name = "Prefetch", Icon = "⚡" }
            ];
        }

        public async Task<CleaningResult> ExecuteDeepCleanAsync(
            List<CleaningCategory> selectedCategories,
            IProgress<(string status, int percent, long bytesFreed)>? progress = null,
            CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var result = new CleaningResult();
            int totalSteps = selectedCategories.Count(c => c.IsSelected);
            int currentStep = 0;

            var cleanActions = new Dictionary<string, Func<CleaningCategory, CancellationToken, Task>>
            {
                ["Fichiers temporaires Windows"] = CleanWindowsTempAsync,
                ["Cache navigateurs"] = CleanBrowserCachesAsync,
                ["Logs système"] = CleanSystemLogsAsync,
                ["Corbeille"] = CleanRecycleBinAsync,
                ["Fichiers temporaires utilisateur"] = CleanUserTempAsync,
                ["Cache miniatures"] = CleanThumbnailCacheAsync,
                ["Rapports d'erreur Windows"] = CleanErrorReportsAsync,
                ["Fichiers d'installation obsolètes"] = CleanObsoleteInstallersAsync,
                ["Cache DNS"] = FlushDnsCacheAsync,
                ["Prefetch"] = CleanPrefetchAsync
            };

            foreach (var category in selectedCategories.Where(c => c.IsSelected))
            {
                ct.ThrowIfCancellationRequested();
                currentStep++;
                int percent = (int)((double)currentStep / totalSteps * 100);
                progress?.Report(($"Nettoyage : {category.Name}...", percent, result.TotalBytesFreed));

                try
                {
                    if (cleanActions.TryGetValue(category.Name, out var action))
                    {
                        await action(category, ct);
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

        private async Task CleanWindowsTempAsync(CleaningCategory cat, CancellationToken ct)
        {
            string winTemp = Path.GetTempPath();
            string systemTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");

            await CleanDirectorySafeAsync(winTemp, cat, ct);
            await CleanDirectorySafeAsync(systemTemp, cat, ct);
        }

        private async Task CleanUserTempAsync(CleaningCategory cat, CancellationToken ct)
        {
            string userTemp = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
            await CleanDirectorySafeAsync(userTemp, cat, ct);
        }

        private async Task CleanBrowserCachesAsync(CleaningCategory cat, CancellationToken ct)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string[] browserCachePaths =
            [
                Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache"),
                Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Code Cache"),
                Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache"),
                Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Code Cache"),
                Path.Combine(localAppData, @"Mozilla\Firefox\Profiles"),
                Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data\Default\Cache"),
                Path.Combine(localAppData, @"Opera Software\Opera Stable\Cache"),
            ];

            foreach (var path in browserCachePaths)
            {
                ct.ThrowIfCancellationRequested();
                if (Directory.Exists(path))
                    await CleanDirectorySafeAsync(path, cat, ct);
            }
        }

        private async Task CleanSystemLogsAsync(CleaningCategory cat, CancellationToken ct)
        {
            string[] logPaths =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\LogFiles"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\WER"),
            ];

            foreach (var path in logPaths)
            {
                ct.ThrowIfCancellationRequested();
                if (Directory.Exists(path))
                    await CleanOldFilesAsync(path, TimeSpan.FromDays(7), cat, ct);
            }
        }

        private async Task CleanRecycleBinAsync(CleaningCategory cat, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo("powershell", "-Command \"Clear-RecycleBin -Force -ErrorAction SilentlyContinue\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(15000);
                    cat.FilesDeleted += 1;
                }
                catch (Exception ex)
                {
                    LoggingService.LogException(ex, "DeepCleaning.RecycleBin");
                }
            }, ct);
        }

        private async Task CleanThumbnailCacheAsync(CleaningCategory cat, CancellationToken ct)
        {
            string thumbCache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Windows\Explorer");

            await Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(thumbCache)) return;
                    foreach (var file in Directory.GetFiles(thumbCache, "thumbcache_*.db"))
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            var fi = new FileInfo(file);
                            cat.BytesFreed += fi.Length;
                            fi.Delete();
                            cat.FilesDeleted++;
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogException(ex, "DeepCleaning.ThumbnailCache");
                }
            }, ct);
        }

        private async Task CleanErrorReportsAsync(CleaningCategory cat, CancellationToken ct)
        {
            string[] werPaths =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Windows\WER"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"Microsoft\Windows\WER"),
            ];

            foreach (var path in werPaths)
            {
                ct.ThrowIfCancellationRequested();
                if (Directory.Exists(path))
                    await CleanDirectorySafeAsync(path, cat, ct);
            }
        }

        private async Task CleanObsoleteInstallersAsync(CleaningCategory cat, CancellationToken ct)
        {
            string installerCache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Installer");
            string softwareDist = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"SoftwareDistribution\Download");

            if (Directory.Exists(softwareDist))
                await CleanOldFilesAsync(softwareDist, TimeSpan.FromDays(30), cat, ct);
        }

        private async Task FlushDnsCacheAsync(CleaningCategory cat, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo("ipconfig", "/flushdns")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(5000);
                    cat.FilesDeleted += 1;
                }
                catch (Exception ex)
                {
                    LoggingService.LogException(ex, "DeepCleaning.FlushDns");
                }
            }, ct);
        }

        private async Task CleanPrefetchAsync(CleaningCategory cat, CancellationToken ct)
        {
            string prefetch = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
            if (Directory.Exists(prefetch))
                await CleanOldFilesAsync(prefetch, TimeSpan.FromDays(14), cat, ct);
        }

        private static async Task CleanDirectorySafeAsync(string path, CleaningCategory cat, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                if (!Directory.Exists(path)) return;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            var fi = new FileInfo(file);
                            if (fi.LastWriteTimeUtc > DateTime.UtcNow.AddHours(-1)) continue;
                            cat.BytesFreed += fi.Length;
                            fi.Delete();
                            cat.FilesDeleted++;
                        }
                        catch { }
                    }

                    foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                        .OrderByDescending(d => d.Length))
                    {
                        try
                        {
                            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                                Directory.Delete(dir);
                        }
                        catch { }
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
                if (!Directory.Exists(path)) return;
                var cutoff = DateTime.UtcNow - maxAge;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            var fi = new FileInfo(file);
                            if (fi.LastWriteTimeUtc < cutoff)
                            {
                                cat.BytesFreed += fi.Length;
                                fi.Delete();
                                cat.FilesDeleted++;
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogException(ex, $"DeepCleaning.CleanOldFiles({path})");
                }
            }, ct);
        }

        public async Task<long> EstimateCleanableBytesAsync(CancellationToken ct = default)
        {
            long total = 0;

            await Task.Run(() =>
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

                string[] paths =
                [
                    Path.GetTempPath(),
                    Path.Combine(windows, "Temp"),
                    Path.Combine(local, "Temp"),
                    Path.Combine(local, @"Google\Chrome\User Data\Default\Cache"),
                    Path.Combine(local, @"Google\Chrome\User Data\Default\Code Cache"),
                    Path.Combine(local, @"Microsoft\Edge\User Data\Default\Cache"),
                    Path.Combine(local, @"Microsoft\Windows\WER"),
                    Path.Combine(appData, @"Microsoft\Windows\WER"),
                    Path.Combine(windows, "Logs"),
                    Path.Combine(windows, @"SoftwareDistribution\Download"),
                ];

                foreach (var path in paths)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!Directory.Exists(path)) continue;
                    try
                    {
                        total += Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                            .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
                    }
                    catch { }
                }

                try
                {
                    foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                    {
                        var recyclePath = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
                        if (!Directory.Exists(recyclePath)) continue;
                        total += Directory.EnumerateFiles(recyclePath, "*", SearchOption.AllDirectories)
                            .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
                    }
                }
                catch { }
            }, ct);

            return total;
        }
    }
}
