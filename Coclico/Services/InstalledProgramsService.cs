using Microsoft.Win32;
using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Coclico.Services
{
    public partial class InstalledProgramsService
    {
        private static readonly SearchValues<string> BlacklistValues = SearchValues.Create(
            [
                "driver", "pilote", "redistributable", "runtime", "framework", "sdk", "update", "helper", 
                "service", "agent", "bootstrapper", "component", "library", "module", "engine", 
                "diagnostics", "verifier", "profiler", "debugger", "compiler", "intellisense", 
                "extension", "anticheat", "toolkit", "vcredist", "directx", "msvc",
                "soundtrack", "controller", "utility", "msi installer", "setup", "uninstaller",
                "application verifier", "diagnostics hub", "iscsi initiator", "odbc data sources",
                "character map", "perfmon", "resmon", "sysprep", "vssui", "system diagnostics",
                "application loader host", "dynamic_lighting_lib", "rgb_sync_control", "powerplan",
                "wpt redistributables", "microsoft verifier", "clickonce bootstrapper", 
                "dynamic application loader", "ene video capture", "marvell_hal", "system verifier"
            ], StringComparison.OrdinalIgnoreCase);

        private static readonly FrozenSet<string> KnownGamesSet = 
            new[] { "genshin", "honkai", "star rail", "valorant", "league of legends", "overwatch", "minecraft", "roblox", "dota", "warcraft" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        [GeneratedRegex("\"DisplayName\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex EpicNameRegex();
        [GeneratedRegex("\"InstallLocation\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex EpicPathRegex();
        [GeneratedRegex("\"LaunchExecutable\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex EpicExeRegex();
        [GeneratedRegex("\"name\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex SteamNameRegex();
        [GeneratedRegex("\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex SteamDirRegex();

        private static readonly Lazy<InstalledProgramsService> _lazyInstance = new(() => new InstalledProgramsService());
        public static InstalledProgramsService Instance => _lazyInstance.Value;

        private List<ProgramInfo>? _memoryCache;

        private Dictionary<string, CustomAppEntry>? _customAppsDataCache;

        private static readonly JsonSerializerOptions _jsonCaseInsensitive = new() { PropertyNameCaseInsensitive = true };
        private static readonly JsonSerializerOptions _jsonWriteIndented   = new() { WriteIndented = true };

        public void InvalidateMemoryCache() => _memoryCache = null;

        public List<string> GetMemoryCacheIconPaths()
        {
            return _memoryCache?.Where(p => !string.IsNullOrEmpty(p.IconPath))
                                .Select(p => p.IconPath)
                                .Distinct()
                                .ToList() ?? [];
        }

        public class ProgramInfo
        {
            public string Name { get; set; } = string.Empty;
            public string InstallPath { get; set; } = string.Empty;
            public string ExePath { get; set; } = string.Empty;
            public string Source { get; set; } = "Windows";
            public string Publisher { get; set; } = string.Empty;
            public string Version { get; set; } = string.Empty;
            public long SizeBytes { get; set; }
            public string IconPath { get; set; } = string.Empty;
            public string Category { get; set; } = "Logiciel";
        }

        public class FilterGroup : System.ComponentModel.INotifyPropertyChanged
        {
            private string _name = string.Empty;
            private bool _showsGames;
            private string _categoryFilter = "Tout";

            public string Name 
            { 
                get => _name; 
                set { _name = value; OnPropertyChanged(); } 
            }
            public bool IsStatic { get; set; }
            public bool ShowsGames 
            { 
                get => _showsGames; 
                set { _showsGames = value; OnPropertyChanged(); } 
            }
            public string CategoryFilter 
            { 
                get => _categoryFilter; 
                set { _categoryFilter = value; OnPropertyChanged(); } 
            }

            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            {
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
            }
        }

        private static string CategoriesSettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico", "categories.json");
        private static string FilterGroupsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico", "filter_groups.json");

        private static readonly Dictionary<string, string> _categoryMigration = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Games"]       = "Jeux",
            ["Office"]      = "Professionnel",
            ["Utilities"]   = "Logiciel",
            ["Development"] = "Professionnel",
            ["Utility"]     = "Logiciel",
            ["Software"]    = "Logiciel",
            ["Tools"]       = "Outils Windows",
            ["System"]      = "Outils Windows",
        };

        private static string MigrateCategory(string name) =>
            _categoryMigration.TryGetValue(name, out var migrated) ? migrated : name;

        public List<string> GetCategories()
        {
            try
            {
                if (File.Exists(CategoriesSettingsPath))
                {
                    using var fs = File.OpenRead(CategoriesSettingsPath);
                    var loaded = JsonSerializer.Deserialize<List<string>>(fs, _jsonCaseInsensitive);
                    if (loaded != null)
                    {
                        var migrated = loaded
                            .Select(MigrateCategory)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        foreach (var def in GetDefaultCategories())
                            if (!migrated.Contains(def, StringComparer.OrdinalIgnoreCase))
                                migrated.Add(def);

                        if (!migrated.SequenceEqual(loaded, StringComparer.OrdinalIgnoreCase))
                            SaveCategories(migrated);

                        return migrated;
                    }
                }
            }
            catch { }
            return GetDefaultCategories();
        }

        private static List<string> GetDefaultCategories() =>
            ["Tout", "Jeux", "Logiciel", "Video Montage", "Professionnel", "Outils Windows", "Pilote", "Composant"];

        public void SaveCategories(List<string> categories)
        {
            try
            {
                string dir = Path.GetDirectoryName(CategoriesSettingsPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string content = System.Text.Json.JsonSerializer.Serialize(categories.Take(255).ToList());
                File.WriteAllText(CategoriesSettingsPath, content);
            }
            catch (Exception ex) { LoggingService.LogException(ex, "InstalledProgramsService.SaveCategories"); }
        }

        public List<FilterGroup> GetFilterGroups()
        {
            try
            {
                if (File.Exists(FilterGroupsPath))
                {
                    string content = File.ReadAllText(FilterGroupsPath);
                    var groups = System.Text.Json.JsonSerializer.Deserialize<List<FilterGroup>>(content) ?? GetDefaultFilterGroups();
                    bool dirty = false;
                    foreach (var g in groups)
                    {
                        var migrated = MigrateCategory(g.CategoryFilter);
                        if (migrated != g.CategoryFilter) { g.CategoryFilter = migrated; dirty = true; }
                    }
                    if (dirty) SaveFilterGroups(groups);
                    return groups;
                }
            }
            catch { }
            return GetDefaultFilterGroups();
        }

        private static List<FilterGroup> GetDefaultFilterGroups() =>
        [
            new FilterGroup { Name = "Bibliothèque", IsStatic = true, ShowsGames = false },
            new FilterGroup { Name = "Gaming Hub", IsStatic = false, ShowsGames = true }
        ];

        public void SaveFilterGroups(List<FilterGroup> groups)
        {
            try
            {
                string dir = Path.GetDirectoryName(FilterGroupsPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var toSave = groups.Where(g => g.Name != "Bibliothèque").Take(4).ToList();
                toSave.Insert(0, new FilterGroup { Name = "Bibliothèque", IsStatic = true });
                string content = System.Text.Json.JsonSerializer.Serialize(toSave);
                File.WriteAllText(FilterGroupsPath, content);
            }
            catch (Exception ex) { LoggingService.LogException(ex, "InstalledProgramsService.SaveFilterGroups"); }
        }

        public async ValueTask<List<ProgramInfo>> GetAllInstalledProgramsAsync(
            bool forceRefresh = false,
            System.Threading.CancellationToken cancellationToken = default)
        {
            if (!forceRefresh && _memoryCache != null)
                return _memoryCache;

            if (!forceRefresh)
            {
                try
                {
                    var cacheKey = "programs_scan_v1";
                    var cached = await CacheService.Instance.GetAsync<List<ProgramInfo>>(cacheKey).ConfigureAwait(false);
                    if (cached != null && cached.Count > 0)
                    {
                        var cachedCustomData = LoadCustomAppsData();
                        foreach (var p in cached)
                        {
                            if (TryGetCustomAppEntry(cachedCustomData, p, out var entry))
                            {
                                if (entry.Name     != null) p.Name     = entry.Name;
                                if (entry.Category != null) p.Category = entry.Category;
                            }
                        }
                        _memoryCache = cached;
                        return cached;
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogException(ex, "InstalledProgramsService.GetAllInstalledProgramsAsync.CacheRead");
                }
            }

            var rawList = new List<ProgramInfo>(512);
            var customData = LoadCustomAppsData();

            var scans = new Func<System.Threading.CancellationToken, List<ProgramInfo>>[]
            {
                ScanEpicGames,
                ScanSteam,
                ScanGOG,
                ScanUbisoft,
                ScanEA,
                ScanRockstar,
                ScanUWPApps,
                ScanStartMenu,
                ScanRegistryUninstall
            };

            int maxPar = Math.Max(1, Environment.ProcessorCount);
            using var sem = new System.Threading.SemaphoreSlim(maxPar);
            var tasks = scans.Select(scan => Task.Run(async () =>
            {
                await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
                try { return scan(cancellationToken); }
                finally { sem.Release(); }
            }, cancellationToken)).ToArray();

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            var byExe  = new Dictionary<string, ProgramInfo>(512, StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, ProgramInfo>(512, StringComparer.OrdinalIgnoreCase);

            static bool IsBetterSource(ProgramInfo existing, ProgramInfo candidate) =>
                existing.Source.Equals("Windows", StringComparison.OrdinalIgnoreCase) &&
                !candidate.Source.Equals("Windows", StringComparison.OrdinalIgnoreCase);

            foreach (var result in results)
            {
                foreach (var app in result)
                {
                    string normName = NormalizeName(app.Name);
                    string normPath = app.InstallPath.ToLower().TrimEnd('\\');
                    string nameKey  = normName + "|" + normPath;

                    if (!string.IsNullOrEmpty(app.ExePath) && byExe.TryGetValue(app.ExePath, out var ex1))
                    {
                        if (IsBetterSource(ex1, app)) { byExe[app.ExePath] = app; byName[nameKey] = app; }
                        continue;
                    }

                    if (!string.IsNullOrEmpty(normName) && byName.TryGetValue(nameKey, out var ex2))
                    {
                        if (IsBetterSource(ex2, app))
                        {
                            byName[nameKey] = app;
                            if (!string.IsNullOrEmpty(app.ExePath)) byExe[app.ExePath] = app;
                        }
                        continue;
                    }

                    rawList.Add(app);
                    if (!string.IsNullOrEmpty(app.ExePath)) byExe[app.ExePath]  = app;
                    if (!string.IsNullOrEmpty(normName))   byName[nameKey] = app;
                }
            }

            foreach (var item in rawList)
                NormalizeProgramInfo(item);

            var finalizedList = rawList
                .Where(p => IsUserFacingApplication(p))
                .GroupBy(p => {
                    string normName = NormalizeName(p.Name);
                    var words = normName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    string groupKey = words.Length >= 2 ? words[0] + " " + words[1] : normName;

                    if ((p.InstallPath ?? string.Empty).Contains("Program Files", StringComparison.OrdinalIgnoreCase))
                        return groupKey + "_" + (p.InstallPath ?? string.Empty).ToLower();
                    
                    return groupKey;
                })
                .Select(g => {
                    if (g.Count() > 1)
                    {
                        var best = g.FirstOrDefault(x => 
                             x.Name.Contains("Center", StringComparison.OrdinalIgnoreCase) || 
                             x.Name.Contains("Control", StringComparison.OrdinalIgnoreCase) ||
                             x.Name.Contains("Launcher", StringComparison.OrdinalIgnoreCase) ||
                             x.Name.Contains("Desktop", StringComparison.OrdinalIgnoreCase));
                        
                        return best ?? g.OrderByDescending(x => x.SizeBytes).First();
                    }
                    return g.First();
                })
                .OrderBy(p => p.Name)
                .ToList();

            finalizedList.AddRange(LoadManualApplications());

            foreach (var p in finalizedList)
            {
                p.Category = DetermineCategory(p);

                if (TryGetCustomAppEntry(customData, p, out var entry))
                {
                    if (entry.Name     != null) p.Name     = entry.Name;
                    if (entry.Category != null) p.Category = entry.Category;
                }
            }

            _memoryCache = finalizedList;

            try
            {
                var cacheKey = "programs_scan_v1";
                var ttl = TimeSpan.FromHours(6);
                await CacheService.Instance.SetAsync(cacheKey, finalizedList, ttl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "InstalledProgramsService.GetAllInstalledProgramsAsync.CacheWrite");
            }

            return finalizedList;
        }

        private static void NormalizeProgramInfo(ProgramInfo p)
        {
            if (p == null) return;
            p.Name = p.Name ?? string.Empty;
            p.Publisher = p.Publisher ?? string.Empty;
            p.InstallPath = p.InstallPath ?? string.Empty;
            p.ExePath = p.ExePath ?? string.Empty;
            p.Version = p.Version ?? string.Empty;
            p.Source = p.Source ?? string.Empty;
            p.IconPath = p.IconPath ?? string.Empty;
            p.Category = p.Category ?? string.Empty;
        }

        private static string DetermineCategory(ProgramInfo p)
        {
            string name = p.Name.ToLower();
            string path = p.InstallPath.ToLower();
            string pub = p.Publisher.ToLower();

            if (p.Source == "Steam" || p.Source == "Epic Games" || p.Source == "GOG" || p.Source == "Ubisoft" || p.Source == "EA" || p.Source == "Rockstar")
                return "Jeux";

            if (name.Contains("driver") || name.Contains("pilote") || name.Contains("nvidia") || name.Contains("amd") || name.Contains("realtek"))
                return "Pilote";

            if (name.Contains("center") || name.Contains("control") || name.Contains("icue") || name.Contains("hub") || pub.Contains("gigabyte"))
                return "Composant";

            if (name.Contains("adobe") || name.Contains("premiere") || name.Contains("vegas") || name.Contains("da vinci") || name.Contains("blender") || name.Contains("editing") || name.Contains("montage"))
                return "Video Montage";

            if (name.Contains("visual studio") || name.Contains("code") || name.Contains("docker") || name.Contains("sql") || name.Contains("office") || name.Contains("excel") || name.Contains("word"))
                return "Professionnel";

            if (path.Contains("\\windows\\") || name.Contains("system") || p.Source == "Microsoft Store")
                return "Outils Windows";

            if (IsKnownGame(p.Name) || path.Contains("games") || path.Contains("jeux"))
                return "Jeux";

            return "Logiciel";
        }

        private static List<ProgramInfo> ScanEA(System.Threading.CancellationToken cancellationToken)
        {
            var list = new List<ProgramInfo>();
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Electronic Arts\EA Core\Installed Games");
                if (key == null) return list;

                foreach (var subkeyName in key.GetSubKeyNames())
                {
                    if (cancellationToken.IsCancellationRequested) return list;
                    using var subkey = key.OpenSubKey(subkeyName);
                    if (subkey == null) continue;

                    string? path = subkey.GetValue("Install Dir") as string;
                    if (string.IsNullOrEmpty(path)) continue;

                    string name = Path.GetFileName(path.TrimEnd('\\'));
                    string? exePath = FindMainExecutable(path);

                    list.Add(new ProgramInfo
                    {
                        Name = name,
                        InstallPath = path,
                        ExePath = exePath ?? string.Empty,
                        Source = "EA",
                        Publisher = "Electronic Arts",
                        IconPath = exePath ?? string.Empty
                    });
                }
            }
            catch { }
            return list;
        }

        private static List<ProgramInfo> ScanRockstar(System.Threading.CancellationToken cancellationToken)
        {
            var list = new List<ProgramInfo>();
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V");
                if (key != null)
                {
                    if (cancellationToken.IsCancellationRequested) return list;
                    string? path = key.GetValue("InstallFolder") as string;
                    if (!string.IsNullOrEmpty(path))
                    {
                        string? exePath = FindMainExecutable(path);
                        list.Add(new ProgramInfo { Name = "Grand Theft Auto V", InstallPath = path, ExePath = exePath ?? string.Empty, Source = "Rockstar", Publisher = "Rockstar Games", IconPath = exePath ?? string.Empty });
                    }
                }
                
                using var keyRDR2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Rockstar Games\Red Dead Redemption 2");
                if (keyRDR2 != null)
                {
                    if (cancellationToken.IsCancellationRequested) return list;
                    string? path = keyRDR2.GetValue("InstallFolder") as string;
                    if (!string.IsNullOrEmpty(path))
                    {
                        string? exePath = FindMainExecutable(path);
                        list.Add(new ProgramInfo { Name = "Red Dead Redemption 2", InstallPath = path, ExePath = exePath ?? string.Empty, Source = "Rockstar", Publisher = "Rockstar Games", IconPath = exePath ?? string.Empty });
                    }
                }
            }
            catch { }
            return list;
        }

        private static bool IsUserFacingApplication(ProgramInfo p)
        {
            string name = p.Name;
            string pub = p.Publisher;
            string path = p.InstallPath;
            string exe = p.ExePath;

            if (name.AsSpan().ContainsAny(BlacklistValues))
            {
                if (!name.Contains("center", StringComparison.OrdinalIgnoreCase) && 
                    !name.Contains("control", StringComparison.OrdinalIgnoreCase) && 
                    !name.Contains("icue", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (path.Contains("\\windows\\system32", StringComparison.OrdinalIgnoreCase) || 
                path.Contains("\\windows\\syswow64", StringComparison.OrdinalIgnoreCase) || 
                path.Contains("\\windows\\winsxs", StringComparison.OrdinalIgnoreCase) || 
                path.Contains("\\windows\\installer", StringComparison.OrdinalIgnoreCase) ||
                exe.Contains("\\windows\\system32", StringComparison.OrdinalIgnoreCase) || 
                exe.Contains("\\windows\\syswow64", StringComparison.OrdinalIgnoreCase))
            {
                string[] systemAllowList = { "notepad", "calc", "cmd", "powershell", "paint", "snippingtool", "write", "taskmgr", "control" };
                bool isAllowed = false;
                foreach (var a in systemAllowList)
                {
                    if (name.Contains(a, StringComparison.OrdinalIgnoreCase)) { isAllowed = true; break; }
                }

                if (p.Source != "Windows") return false;
                if (!isAllowed) return false;
            }

            string[] systemPubs = { "intel corporation", "advanced micro devices", "realtek semiconductor", "ene technology", "microsoft corporation", "google llc" };
            foreach (var sp in systemPubs)
            {
                if (pub.Contains(sp, StringComparison.OrdinalIgnoreCase))
                {
                    if (!name.Contains("center", StringComparison.OrdinalIgnoreCase) && 
                        !name.Contains("control", StringComparison.OrdinalIgnoreCase) && 
                        !name.Contains("dashboard", StringComparison.OrdinalIgnoreCase) && 
                        !name.Contains("game", StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }

            if (name.Length < 2) return false;
            if (name.Contains("msiexec", StringComparison.OrdinalIgnoreCase)) return false;

            return true;
        }

        private static List<ProgramInfo> ScanStartMenu(System.Threading.CancellationToken cancellationToken)
        {
            var list = new List<ProgramInfo>();
            var paths = new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
            };

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return list;
            dynamic shell;
            try { shell = Activator.CreateInstance(shellType)!; }
            catch (Exception ex) { LoggingService.LogException(ex, "ScanStartMenu.CreateShell"); return list; };

            foreach (var rootPath in paths)
            {
                if (cancellationToken.IsCancellationRequested) return list;
                if (!Directory.Exists(rootPath)) continue;

                var shortcuts = new List<string>();
                AccumulateShortcutsSafely(rootPath, shortcuts);

                foreach (var shortcutPath in shortcuts)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        dynamic shortcut = shell.CreateShortcut(shortcutPath);
                        string targetPath = shortcut.TargetPath;

                        if (string.IsNullOrEmpty(targetPath) || !targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!File.Exists(targetPath)) continue;

                        string name = Path.GetFileNameWithoutExtension(shortcutPath);
                        
                        list.Add(new ProgramInfo
                        {
                            Name = name,
                            ExePath = targetPath,
                            InstallPath = Path.GetDirectoryName(targetPath) ?? string.Empty,
                            Source = "Windows",
                            IconPath = targetPath,
                            SizeBytes = new FileInfo(targetPath).Length
                        });
                    }
                    catch { }
                }
            }
            return list;
        }

        private static List<ProgramInfo> ScanRegistryUninstall(System.Threading.CancellationToken cancellationToken)
        {
            var list = new List<ProgramInfo>();
            string[] registryPaths = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            var rootKeys = new List<RegistryKey> { Registry.LocalMachine, Registry.CurrentUser };

            foreach (var root in rootKeys)
            {
                foreach (var path in registryPaths)
                {
                    try
                    {
                        using var key = root.OpenSubKey(path);
                        if (key == null) continue;

                        foreach (var subkeyName in key.GetSubKeyNames())
                        {
                            using var subkey = key.OpenSubKey(subkeyName);
                            if (subkey == null) continue;

                            string? name = subkey.GetValue("DisplayName") as string;
                            if (string.IsNullOrEmpty(name)) continue;

                            string? installLocation = subkey.GetValue("InstallLocation") as string;
                            string? publisher = subkey.GetValue("Publisher") as string;
                            string? version = subkey.GetValue("DisplayVersion") as string;
                            
                            string exePath = string.Empty;
                            if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                            {
                                exePath = FindMainExecutable(installLocation) ?? string.Empty;
                            }

                            list.Add(new ProgramInfo
                            {
                                Name = name,
                                Publisher = publisher ?? string.Empty,
                                Version = version ?? string.Empty,
                                InstallPath = installLocation ?? string.Empty,
                                ExePath = exePath,
                                Source = "Windows",
                                IconPath = exePath
                            });
                        }
                    }
                    catch { }
                }
            }
            return list;
        }

        private static List<ProgramInfo> ScanEpicGames(System.Threading.CancellationToken cancellationToken)
        {
            var list = new List<ProgramInfo>();
            string epicManifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");

            if (!Directory.Exists(epicManifests)) return list;

            foreach (var file in Directory.GetFiles(epicManifests, "*.item"))
            {
                if (cancellationToken.IsCancellationRequested) break;
                try
                {
                    string content = File.ReadAllText(file);
                    var nameMatch = EpicNameRegex().Match(content);
                    var pathMatch = EpicPathRegex().Match(content);
                    var exeMatch = EpicExeRegex().Match(content);

                    if (nameMatch.Success && pathMatch.Success)
                    {
                        string installDir = pathMatch.Groups[1].Value.Replace("\\\\", "\\");
                        string exeName = exeMatch.Success ? exeMatch.Groups[1].Value : "";
                        string exePath = Path.Combine(installDir, exeName);

                        list.Add(new ProgramInfo
                        {
                            Name = nameMatch.Groups[1].Value,
                            InstallPath = installDir,
                            ExePath = exePath,
                            Source = "Epic Games",
                            Publisher = "Epic",
                            IconPath = exePath
                        });
                    }
                }
                catch { }
            }
            return list;
        }

        private static List<ProgramInfo> ScanSteam(System.Threading.CancellationToken cancellationToken)
        {
            var list = new List<ProgramInfo>();
            string? steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "") as string;

            if (string.IsNullOrEmpty(steamPath)) return list;

            string appsPath = Path.Combine(steamPath, "steamapps");
            if (!Directory.Exists(appsPath)) return list;

            foreach (var file in Directory.GetFiles(appsPath, "appmanifest_*.acf"))
            {
                if (cancellationToken.IsCancellationRequested) break;
                try
                {
                    string content = File.ReadAllText(file);
                    var nameMatch = SteamNameRegex().Match(content);
                    var dirMatch = SteamDirRegex().Match(content);

                    if (nameMatch.Success && dirMatch.Success)
                    {
                        string name = nameMatch.Groups[1].Value;
                        string installDir = Path.Combine(appsPath, "common", dirMatch.Groups[1].Value);
                        string? exePath = FindMainExecutable(installDir);

                        list.Add(new ProgramInfo
                        {
                            Name = name,
                            InstallPath = installDir,
                            ExePath = exePath ?? string.Empty,
                            Source = "Steam",
                            Publisher = "Steam",
                            IconPath = exePath ?? string.Empty
                        });
                    }
                }
                catch { }
            }
            return list;
        }

        private static List<ProgramInfo> ScanGOG(System.Threading.CancellationToken cancellationToken)
        {
            var list = new List<ProgramInfo>();
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
            if (key == null) return list;

            foreach (var subkeyName in key.GetSubKeyNames())
            {
                using var subkey = key.OpenSubKey(subkeyName);
                if (subkey == null) continue;

                string? name = subkey.GetValue("gameName") as string;
                string? path = subkey.GetValue("path") as string;
                string? exe = subkey.GetValue("exe") as string;

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) continue;

                string exePath = string.IsNullOrEmpty(exe) ? (FindMainExecutable(path) ?? string.Empty) : Path.Combine(path, exe);

                list.Add(new ProgramInfo
                {
                    Name = name,
                    InstallPath = path,
                    ExePath = exePath,
                    Source = "GOG",
                    Publisher = "GOG",
                    IconPath = exePath
                });
            }
            return list;
        }

        private static List<ProgramInfo> ScanUbisoft(System.Threading.CancellationToken cancellationToken)
        {
            var list = new List<ProgramInfo>();
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
            if (key == null) return list;
            foreach (var subkeyName in key.GetSubKeyNames())
            {
                if (cancellationToken.IsCancellationRequested) return list;
                using var subkey = key.OpenSubKey(subkeyName);
                if (subkey == null) continue;

                string? path = subkey.GetValue("InstallDir") as string;
                if (string.IsNullOrEmpty(path)) continue;

                string name = Path.GetFileName(path.TrimEnd('\\'));
                string? exePath = FindMainExecutable(path);

                list.Add(new ProgramInfo
                {
                    Name = name,
                    InstallPath = path,
                    ExePath = exePath ?? string.Empty,
                    Source = "Ubisoft",
                    Publisher = "Ubisoft",
                    IconPath = exePath ?? string.Empty
                });
            }
            return list;
        }

        private static List<ProgramInfo> ScanUWPApps(System.Threading.CancellationToken cancellationToken)
        {
            var list = new List<ProgramInfo>();
            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return list;
                dynamic shell = Activator.CreateInstance(shellType)!;
                var folder = shell.NameSpace("shell:AppsFolder");
                foreach (var item in folder.Items())
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    string name = item.Name;
                    string path = item.Path;

                    if (path.Contains('!'))
                    {
                        list.Add(new ProgramInfo
                        {
                            Name = name,
                            InstallPath = string.Empty,
                            ExePath = path,
                            Source = "Microsoft Store",
                            Publisher = "Microsoft",
                        });
                    }
                }
            }
            catch (Exception ex) { LoggingService.LogException(ex, "ScanUWPApps"); }
            return list;
        }

        private static string OverridesPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico", "name_overrides.json");
        private static string CategoryOverridesPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico", "category_overrides.json");

        private static string CustomAppsDataPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Coclico", "custom_apps_data.json");

        private sealed record CustomAppEntry(string? Name, string? Category);

        private static string BuildCustomAppKey(string exePath, string installPath, string name)
        {
            if (!string.IsNullOrWhiteSpace(exePath))
                return $"exe:{exePath.Trim()}";

            if (!string.IsNullOrWhiteSpace(installPath))
                return $"install:{installPath.TrimEnd('\\')}";

            return $"name:{NormalizeName(name)}";
        }

        private static bool TryGetCustomAppEntry(
            Dictionary<string, CustomAppEntry> data,
            ProgramInfo app,
            out CustomAppEntry entry)
        {
            var exeKey = BuildCustomAppKey(app.ExePath, string.Empty, string.Empty);
            if (data.TryGetValue(exeKey, out entry!))
                return true;

            var fallbackKey = BuildCustomAppKey(string.Empty, app.InstallPath, app.Name);
            return data.TryGetValue(fallbackKey, out entry!);
        }

        private Dictionary<string, CustomAppEntry> LoadCustomAppsData()
        {
            if (_customAppsDataCache != null) return _customAppsDataCache;
            try
            {
                if (File.Exists(CustomAppsDataPath))
                {
                    var raw = JsonSerializer.Deserialize<Dictionary<string, CustomAppEntry>>(
                        File.ReadAllText(CustomAppsDataPath), _jsonCaseInsensitive);
                    if (raw != null)
                    {
                        var result = new Dictionary<string, CustomAppEntry>(StringComparer.OrdinalIgnoreCase);
                        bool migrated = false;
                        foreach (var kv in raw)
                        {
                            var value = kv.Value ?? new CustomAppEntry(null, null);
                            var migratedCategory = string.IsNullOrWhiteSpace(value.Category)
                                ? value.Category
                                : MigrateCategory(value.Category);

                            if (!string.Equals(migratedCategory, value.Category, StringComparison.Ordinal))
                                migrated = true;

                            result[kv.Key] = new CustomAppEntry(value.Name, migratedCategory);
                        }

                        if (migrated)
                        {
                            string dir = Path.GetDirectoryName(CustomAppsDataPath)!;
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                            File.WriteAllText(CustomAppsDataPath, JsonSerializer.Serialize(result, _jsonWriteIndented));
                        }

                        _customAppsDataCache = result;
                        return result;
                    }
                }
            }
            catch (Exception ex) { LoggingService.LogException(ex, "InstalledProgramsService.LoadCustomAppsData"); }
            _customAppsDataCache = new Dictionary<string, CustomAppEntry>(StringComparer.OrdinalIgnoreCase);
            return _customAppsDataCache;
        }

        public void SaveCustomAppData(string exePath, string? name, string? category)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return;
            SaveCustomAppDataByKey(BuildCustomAppKey(exePath, string.Empty, string.Empty), name, category, exePath);
        }

        public void SaveCustomAppData(string exePath, string installPath, string appName, string? name, string? category)
        {
            var key = BuildCustomAppKey(exePath, installPath, appName);
            var cacheLookupPath = string.IsNullOrWhiteSpace(exePath)
                ? BuildCustomAppKey(string.Empty, installPath, appName)
                : exePath;

            SaveCustomAppDataByKey(key, name, category, cacheLookupPath);
        }

        private void SaveCustomAppDataByKey(string key, string? name, string? category, string cacheLookupPath)
        {
            try
            {
                var data = LoadCustomAppsData();
                if (!string.IsNullOrWhiteSpace(category))
                    category = MigrateCategory(category);

                data.TryGetValue(key, out var existing);
                data[key] = new CustomAppEntry(
                    name     ?? existing?.Name,
                    category ?? existing?.Category);

                string dir = Path.GetDirectoryName(CustomAppsDataPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(CustomAppsDataPath, JsonSerializer.Serialize(data, _jsonWriteIndented));
                _customAppsDataCache = data;

                var cached = _memoryCache?.FirstOrDefault(
                    p => !string.IsNullOrWhiteSpace(p.ExePath)
                        ? p.ExePath.Equals(cacheLookupPath, StringComparison.OrdinalIgnoreCase)
                        : BuildCustomAppKey(string.Empty, p.InstallPath, p.Name)
                            .Equals(cacheLookupPath, StringComparison.OrdinalIgnoreCase));
                if (cached != null)
                {
                    if (name     != null) cached.Name     = name;
                    if (category != null) cached.Category = category;
                }

                try { CacheService.Instance.Invalidate("programs_scan_v1"); } catch { }
            }
            catch (Exception ex) { LoggingService.LogException(ex, "InstalledProgramsService.SaveCustomAppData"); }
        }

        private static string ManualAppsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico", "manual_apps.json");

        public void AddManualApplication(ProgramInfo app)
        {
            var apps = LoadManualApplications();
            if (!apps.Any(a => a.ExePath.Equals(app.ExePath, StringComparison.OrdinalIgnoreCase)))
            {
                apps.Add(app);
                SaveManualApplications(apps);
                _memoryCache = null;
                try { CacheService.Instance.Invalidate("programs_scan_v1"); } catch { }

                string? customName = string.IsNullOrWhiteSpace(app.Name) ? null : app.Name;
                string? customCat  = string.IsNullOrWhiteSpace(app.Category) ? null : app.Category;
                if (customName != null || customCat != null)
                    SaveCustomAppData(app.ExePath, app.InstallPath, app.Name, customName, customCat);
            }
        }

        private static List<ProgramInfo> LoadManualApplications()
        {
            try
            {
                if (File.Exists(ManualAppsPath))
                {
                    string content = File.ReadAllText(ManualAppsPath);
                    return System.Text.Json.JsonSerializer.Deserialize<List<ProgramInfo>>(content) ?? [];
                }
            }
            catch { }
            return [];
        }

        private static void SaveManualApplications(List<ProgramInfo> apps)
        {
            try
            {
                string dir = Path.GetDirectoryName(ManualAppsPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string content = System.Text.Json.JsonSerializer.Serialize(apps);
                File.WriteAllText(ManualAppsPath, content);
            }
            catch (Exception ex) { LoggingService.LogException(ex, "InstalledProgramsService.SaveManualApplications"); }
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            ReadOnlySpan<char> source = name.AsSpan();
            Span<char> buffer = stackalloc char[source.Length];
            int written = 0;
            bool lastWasSpace = false;

            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (c == '™' || c == '©' || c == '®') continue;
                if (c == '_' || c == '.') c = ' ';
                if (c == '(' || c == '[')
                {
                    int endChar = (c == '(') ? ')' : ']';
                    while (i < source.Length && source[i] != endChar) i++;
                    continue;
                }
                if (c == ' ')
                {
                    if (lastWasSpace) continue;
                    lastWasSpace = true;
                }
                else
                {
                    lastWasSpace = false;
                }
                buffer[written++] = c;
            }

            return buffer[..written].Trim().ToString();
        }

        private static string? FindMainExecutable(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath)) return null;
                var files = Directory.GetFiles(folderPath, "*.exe", SearchOption.TopDirectoryOnly);
                return files.OrderByDescending(f => new FileInfo(f).Length)
                           .FirstOrDefault(f => !f.Contains("uninstall", StringComparison.OrdinalIgnoreCase) && 
                                                !f.Contains("crash", StringComparison.OrdinalIgnoreCase))?.ToString();
            }
            catch { return null; }
        }

        private static void AccumulateShortcutsSafely(string path, List<string> results)
        {
            try
            {
                results.AddRange(Directory.GetFiles(path, "*.lnk"));
                foreach (var directory in Directory.GetDirectories(path))
                {
                    AccumulateShortcutsSafely(directory, results);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            catch (Exception) { }
        }

        public static List<ProgramInfo> DetectGames(List<ProgramInfo> allPrograms) =>
            allPrograms.Where(p =>
                p.Source is "Steam" or "Epic Games" or "GOG" or "Ubisoft" or "EA" or "Rockstar" ||
                p.InstallPath.Contains("games", StringComparison.OrdinalIgnoreCase) ||
                p.InstallPath.Contains("jeux", StringComparison.OrdinalIgnoreCase) ||
                IsKnownGame(p.Name) || p.Category == "Jeux"
            ).ToList();

        private static bool IsKnownGame(string name) =>
            KnownGamesSet.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}
