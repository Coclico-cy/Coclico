using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace Coclico.Services;

public class InstallerService
{
    private static readonly SemaphoreSlim _installationLock = new(1, 1);
    private static readonly Regex PackageIdRegex = new("^[A-Za-z0-9][A-Za-z0-9._-]{1,127}$", RegexOptions.Compiled);
    private static readonly Regex ColumnDashesRegex = new(@"-+", RegexOptions.Compiled);
    private static readonly Regex MultiSpaceSplitRegex = new(@"\s{2,}", RegexOptions.Compiled);
    private static string? _cachedWingetPath;

    public static bool IsValidPackageId(string packageId)
    {
        return !string.IsNullOrWhiteSpace(packageId) && PackageIdRegex.IsMatch(packageId.Trim());
    }

    public class WingetPackage : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string AvailableVersion { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Category { get; set; } = "Utilitaires";
        public string Description { get; set; } = string.Empty;

        public bool IsSelected
        {
            get;
            set { field = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get;
            set { field = value; OnPropertyChanged(); }
        } = "Prêt";

        public bool IsBusy
        {
            get;
            set { field = value; OnPropertyChanged(); }
        }

        public bool IsInstalled
        {
            get;
            set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(InstallButtonLabel)); }
        }

        public string InstallButtonLabel => IsInstalled ? "Déjà installé" : "Installer";

        public string WingetId { get => Id; set => Id = value; }
        public double ProgressValue { get; set; }

        public bool HasUpgrade => !string.IsNullOrEmpty(AvailableVersion) && AvailableVersion != Version;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class SoftwareItem : WingetPackage
    {
    }

    public List<WingetPackage> GetAvailableSoftware()
    {
        return GetEssentialPackages();
    }

    public async Task DetectInstalledPackagesAsync(IEnumerable<WingetPackage> packages, CancellationToken ct = default)
    {
        var installedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            installedIds = await Task.Run(async () =>
            {
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var psi = new ProcessStartInfo
                {
                    FileName = GetWingetPath(),
                    Arguments = "list --accept-source-agreements",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                try
                {
                    using var proc = Process.Start(psi);
                    if (proc == null)
                    {
                        return ids;
                    }

                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
                    Task<string> outputTask = proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);
                    Task<string> errorTask = proc.StandardError.ReadToEndAsync(timeoutCts.Token);
                    await Task.WhenAll(outputTask, errorTask, proc.WaitForExitAsync(timeoutCts.Token));
                    string output = await outputTask;

                    string[] lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                    int idColStart = -1;
                    int idColLength = -1;

                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        if (line.TrimStart().StartsWith("---"))
                        {
                            MatchCollection matches = ColumnDashesRegex.Matches(line);
                            if (matches.Count >= 2)
                            {
                                idColStart = matches[1].Index;
                                idColLength = matches[1].Length;
                            }

                            for (int j = i + 1; j < lines.Length; j++)
                            {
                                string dataLine = lines[j];
                                if (string.IsNullOrWhiteSpace(dataLine))
                                {
                                    continue;
                                }

                                string extractedId = string.Empty;
                                if (idColStart >= 0 && dataLine.Length > idColStart)
                                {
                                    extractedId = (idColLength > 0 && dataLine.Length >= idColStart + idColLength)
                                        ? dataLine.Substring(idColStart, idColLength).Trim()
                                        : dataLine[idColStart..].Trim();

                                    string[] tokens = extractedId.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                    if (tokens.Length > 0)
                                    {
                                        extractedId = tokens[0];
                                    }
                                }

                                if (string.IsNullOrWhiteSpace(extractedId))
                                {
                                    string[] parts = MultiSpaceSplitRegex.Split(dataLine.Trim());
                                    if (parts.Length >= 2)
                                    {
                                        extractedId = parts[1];
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(extractedId))
                                {
                                    _ = ids.Add(extractedId);
                                }
                            }
                            break;
                        }
                    }
                }
                catch (Exception ex) { LoggingService.LogException(ex, "InstallerService.DetectInstalled"); }
                return ids;
            }, ct);
        }
        catch (Exception ex) { LoggingService.LogException(ex, "InstallerService.DetectInstalledPackagesAsync"); }

        foreach (WingetPackage pkg in packages)
        {
            pkg.IsInstalled = installedIds.Contains(pkg.Id);
        }
    }

    public List<WingetPackage> GetEssentialPackages()
    {
        return
        [

            new() { Name = "Google Chrome", Id = "Google.Chrome", Category = "Navigateurs", Description = "Le navigateur web le plus populaire au monde" },
            new() { Name = "Mozilla Firefox", Id = "Mozilla.Firefox", Category = "Navigateurs", Description = "Navigateur rapide, libre et respectueux de la vie privée" },
            new() { Name = "Brave Browser", Id = "Brave.Brave", Category = "Navigateurs", Description = "Navigateur ultra-rapide avec bloqueur de pubs natif" },
            new() { Name = "Opera GX", Id = "Opera.OperaGX", Category = "Navigateurs", Description = "Le navigateur conçu spécialement pour les gamers" },
            new() { Name = "Zen Browser", Id = "Zen-Team.Zen-Browser", Category = "Navigateurs", Description = "Navigateur moderne basé sur Firefox axé sur le design" },

            new() { Name = "Steam", Id = "Valve.Steam", Category = "Gaming", Description = "La plateforme incontournable de jeux vidéo sur PC" },
            new() { Name = "Discord", Id = "Discord.Discord", Category = "Gaming", Description = "Communication vocale, vidéo et textuelle pour joueurs" },
            new() { Name = "Epic Games Launcher", Id = "EpicGames.EpicGamesLauncher", Category = "Gaming", Description = "Boutique de jeux Epic Games et Unreal Engine" },
            new() { Name = "Ubisoft Connect", Id = "Ubisoft.Connect", Category = "Gaming", Description = "Lanceur officiel des jeux Ubisoft" },
            new() { Name = "EA App", Id = "ElectronicArts.EADesktop", Category = "Gaming", Description = "Lanceur officiel des jeux Electronic Arts" },
            new() { Name = "GOG Galaxy", Id = "GOG.Galaxy", Category = "Gaming", Description = "Plateforme de jeux sans DRM de CD Projekt" },

            new() { Name = "VLC Media Player", Id = "VideoLAN.VLC", Category = "Multimédia", Description = "Lecteur multimédia universel lisant tous les formats" },
            new() { Name = "Spotify", Id = "Spotify.Spotify", Category = "Multimédia", Description = "Streaming musical et podcasts" },
            new() { Name = "OBS Studio", Id = "OBSProject.OBSStudio", Category = "Multimédia", Description = "Logiciel référence de capture vidéo et de streaming" },
            new() { Name = "Paint.NET", Id = "dotPDN.PaintDotNet", Category = "Multimédia", Description = "Éditeur d'images et de retouche photo puissant" },
            new() { Name = "GIMP", Id = "GIMP.GIMP.2", Category = "Multimédia", Description = "Création graphique et manipulation d'images avancée" },
            new() { Name = "HandBrake", Id = "HandBrake.HandBrake", Category = "Multimédia", Description = "Convertisseur vidéo rapide et performant" },
            new() { Name = "Audacity", Id = "Audacity.Audacity", Category = "Multimédia", Description = "Éditeur et enregistreur audio multipiste" },

            new() { Name = "7-Zip", Id = "7zip.7zip", Category = "Système", Description = "Archiveur de fichiers avec taux de compression élevé" },
            new() { Name = "Microsoft PowerToys", Id = "Microsoft.PowerToys", Category = "Système", Description = "Ensemble d'outils pour améliorer la productivité Windows" },
            new() { Name = "Everything", Id = "voidtools.Everything", Category = "Système", Description = "Moteur de recherche instantané de fichiers sur PC" },
            new() { Name = "WizTree", Id = "AntibodySoftware.WizTree", Category = "Système", Description = "Analyseur d'espace disque le plus rapide pour Windows" },
            new() { Name = "Rufus", Id = "Rufus.Rufus", Category = "Système", Description = "Créateur de clés USB de démarrage Windows et Linux" },
            new() { Name = "CPU-Z", Id = "CPUID.CPU-Z", Category = "Système", Description = "Informations détaillées sur le processeur et la carte mère" },
            new() { Name = "HWMonitor", Id = "CPUID.HWMonitor", Category = "Système", Description = "Surveillance en temps réel des températures et voltages" },
            new() { Name = "CrystalDiskInfo", Id = "CrystalDewWorld.CrystalDiskInfo", Category = "Système", Description = "Surveillance de la santé des disques durs et SSD (S.M.A.R.T.)" },

            new() { Name = "Visual Studio Code", Id = "Microsoft.VisualStudioCode", Category = "Développement", Description = "L'éditeur de code source moderne et extensible" },
            new() { Name = "Git", Id = "Git.Git", Category = "Développement", Description = "Système de contrôle de version distribué incontournable" },
            new() { Name = "GitHub Desktop", Id = "GitHub.GitHubDesktop", Category = "Développement", Description = "Interface graphique intuitive pour gérer vos dépôts Git" },
            new() { Name = "Node.js LTS", Id = "OpenJS.NodeJS.LTS", Category = "Développement", Description = "Environnement d'exécution JavaScript côté serveur" },
            new() { Name = "Python 3.12", Id = "Python.Python.3.12", Category = "Développement", Description = "Langage de programmation polyvalent" },
            new() { Name = "Notepad++", Id = "Notepad++.Notepad++", Category = "Développement", Description = "Éditeur de texte léger avec coloration syntaxique" },

            new() { Name = "Visual C++ 2015-2022 (x64)", Id = "Microsoft.VCRedist.2015+.x64", Category = "Runtimes", Description = "Bibliothèques C++ requises par la plupart des jeux et logiciels" },
            new() { Name = ".NET Desktop Runtime 8", Id = "Microsoft.DotNet.DesktopRuntime.8", Category = "Runtimes", Description = "Runtime moderne pour faire tourner les applications Windows" },

            new() { Name = "LibreOffice", Id = "TheDocumentFoundation.LibreOffice", Category = "Bureautique", Description = "Suite bureautique complète, libre et gratuite" },
            new() { Name = "Adobe Acrobat Reader", Id = "Adobe.Acrobat.Reader.64-bit", Category = "Bureautique", Description = "Lecteur standard de documents PDF" },
        ];
    }

    public async Task<List<WingetPackage>> SearchWingetAsync(string query, CancellationToken ct = default)
    {
        return string.IsNullOrWhiteSpace(query)
            ? []
            : await Task.Run(() =>
        {
            var results = new List<WingetPackage>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = GetWingetPath(),
                    Arguments = $"search \"{query}\" --source winget --accept-source-agreements",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return results;
                }

                string output = proc.StandardOutput.ReadToEnd();
                _ = proc.WaitForExit(15000);

                string[] lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                bool headerPassed = false;

                foreach (string rawLine in lines)
                {
                    ct.ThrowIfCancellationRequested();
                    string line = rawLine.Trim();
                    if (line.StartsWith("---") || line.StartsWith("Nom") || line.StartsWith("Name"))
                    {
                        headerPassed = true;
                        continue;
                    }
                    if (!headerPassed)
                    {
                        continue;
                    }

                    string[] parts = MultiSpaceSplitRegex.Split(line);
                    if (parts.Length >= 2)
                    {
                        string name = parts[0];
                        string id = parts[1];
                        string version = parts.Length > 2 ? parts[2] : "";
                        string source = "winget";

                        results.Add(new WingetPackage
                        {
                            Name = name,
                            Id = id,
                            Version = version,
                            Source = source,
                            Category = "Recherche"
                        });

                        if (results.Count >= 50)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, $"InstallerService.SearchWingetAsync({query})");
            }

            return results;
        }, ct);
    }

    public async Task<List<WingetPackage>> GetAvailableUpgradesAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var upgrades = new List<WingetPackage>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = GetWingetPath(),
                    Arguments = "upgrade --include-unknown --accept-source-agreements",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return upgrades;
                }

                string output = proc.StandardOutput.ReadToEnd();
                _ = proc.WaitForExit(25000);

                string[] lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                bool headerPassed = false;

                foreach (string rawLine in lines)
                {
                    ct.ThrowIfCancellationRequested();
                    string line = rawLine.Trim();
                    if (line.StartsWith("---") || line.StartsWith("Nom") || line.StartsWith("Name"))
                    {
                        headerPassed = true;
                        continue;
                    }
                    if (!headerPassed)
                    {
                        continue;
                    }

                    if ((line.Contains("upgrade", StringComparison.OrdinalIgnoreCase) && line.Contains("available", StringComparison.OrdinalIgnoreCase))
                        || (line.Contains("mise", StringComparison.OrdinalIgnoreCase) && line.Contains("disponible", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    string[] parts = MultiSpaceSplitRegex.Split(line);
                    if (parts.Length >= 4)
                    {
                        string name = parts[0];
                        string id = parts[1];
                        string version = parts[2];
                        string available = parts[3];

                        upgrades.Add(new WingetPackage
                        {
                            Name = name,
                            Id = id,
                            Version = version,
                            AvailableVersion = available,
                            Category = "Mise à jour",
                            IsSelected = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "InstallerService.GetAvailableUpgradesAsync");
            }

            return upgrades;
        }, ct);
    }

    public async Task<bool> InstallPackageAsync(string packageId, Action<string>? onOutput = null, CancellationToken ct = default)
    {
        if (!IsValidPackageId(packageId))
        {
            return false;
        }

        await _installationLock.WaitAsync(ct);
        try
        {
            onOutput?.Invoke($"📦 Téléchargement et installation de {packageId}...");
            string args = $"install --id {packageId} -e --silent --accept-package-agreements --accept-source-agreements --force";
            return await RunWingetCommandAsync(args, onOutput, ct);
        }
        finally
        {
            _ = _installationLock.Release();
        }
    }

    public async Task<bool> UpgradePackageAsync(string packageId, Action<string>? onOutput = null, CancellationToken ct = default)
    {
        if (!IsValidPackageId(packageId))
        {
            return false;
        }

        await _installationLock.WaitAsync(ct);
        try
        {
            onOutput?.Invoke($"🔄 Mise à jour de {packageId}...");
            string args = $"upgrade --id {packageId} -e --silent --accept-package-agreements --accept-source-agreements";
            return await RunWingetCommandAsync(args, onOutput, ct);
        }
        finally
        {
            _ = _installationLock.Release();
        }
    }

    public async Task<bool> UpgradeAllAsync(Action<string>? onOutput = null, CancellationToken ct = default)
    {
        await _installationLock.WaitAsync(ct);
        try
        {
            onOutput?.Invoke("🚀 Démarrage de la mise à jour globale de tous les logiciels installés...");
            string args = "upgrade --all --silent --accept-package-agreements --accept-source-agreements --include-unknown";
            return await RunWingetCommandAsync(args, onOutput, ct);
        }
        finally
        {
            _ = _installationLock.Release();
        }
    }

    public async Task<bool> UninstallPackageAsync(string packageId, Action<string>? onOutput = null, CancellationToken ct = default)
    {
        if (!IsValidPackageId(packageId))
        {
            return false;
        }

        await _installationLock.WaitAsync(ct);
        try
        {
            onOutput?.Invoke($"🗑️ Désinstallation de {packageId}...");
            string args = $"uninstall --id {packageId} --silent";
            return await RunWingetCommandAsync(args, onOutput, ct);
        }
        finally
        {
            _ = _installationLock.Release();
        }
    }

    private async Task<bool> RunWingetCommandAsync(string args, Action<string>? onOutput, CancellationToken ct)
    {
        return await Task.Run(async () =>
        {
            Process? proc = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = GetWingetPath(),
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                proc = new Process { StartInfo = psi };
                proc.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        onOutput?.Invoke(e.Data);
                    }
                };
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        onOutput?.Invoke($"⚠️ {e.Data}");
                    }
                };

                if (!proc.Start())
                {
                    onOutput?.Invoke("❌ Impossible de démarrer le processus WinGet.");
                    return false;
                }

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                await proc.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromMinutes(10), ct);

                int code = proc.ExitCode;

                bool ok = code is 0 or 3010 or 1641 or -1978335215;

                if (ok)
                {
                    onOutput?.Invoke("✅ Opération terminée avec succès.");
                }
                else if (code is -1978335189 or 740)
                {
                    onOutput?.Invoke("⚠️ Droits administrateur requis pour ce paquet. Relancez Coclico en tant qu'administrateur.");
                }
                else if (code == -1978335212)
                {
                    onOutput?.Invoke("ℹ️ Aucune mise à jour requise ou paquet déjà à jour.");
                    ok = true;
                }
                else
                {
                    onOutput?.Invoke($"⚠️ Processus terminé avec le code {code}.");
                }

                return ok;
            }
            catch (OperationCanceledException)
            {
                onOutput?.Invoke("⏹️ Opération annulée par l'utilisateur.");
                try { proc?.Kill(entireProcessTree: true); } catch (Exception ex) { LoggingService.LogException(ex, "InstallerService.RunWingetCommandAsync"); }
                return false;
            }
            catch (TimeoutException)
            {
                onOutput?.Invoke("⏱️ WinGet a dépassé le délai maximal.");
                try { proc?.Kill(entireProcessTree: true); } catch (Exception ex) { LoggingService.LogException(ex, "InstallerService.RunWingetCommandAsync"); }
                return false;
            }
            catch (Exception ex)
            {
                onOutput?.Invoke($"❌ Erreur d'exécution : {ex.Message}");
                LoggingService.LogException(ex, $"RunWingetCommand({args})");
                return false;
            }
            finally
            {
                proc?.Dispose();
            }
        }, ct);
    }

    public bool IsRunAsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public string GetWingetPath()
    {
        if (!string.IsNullOrEmpty(_cachedWingetPath) && File.Exists(_cachedWingetPath))
        {
            return _cachedWingetPath;
        }

        try
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string winApps = Path.Combine(local, @"Microsoft\WindowsApps");
            if (Directory.Exists(winApps))
            {
                string direct = Path.Combine(winApps, "winget.exe");
                if (File.Exists(direct))
                {
                    return _cachedWingetPath = direct;
                }

                string[] dirs = Directory.GetDirectories(winApps, "Microsoft.DesktopAppInstaller_*");
                foreach (string dir in dirs)
                {
                    string candidate = Path.Combine(dir, "winget.exe");
                    if (File.Exists(candidate))
                    {
                        return _cachedWingetPath = candidate;
                    }
                }
            }

            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pfWinApps = Path.Combine(pf, "WindowsApps");
            if (Directory.Exists(pfWinApps))
            {
                string[] dirs = Directory.GetDirectories(pfWinApps, "Microsoft.DesktopAppInstaller_*");
                foreach (string dir in dirs)
                {
                    string candidate = Path.Combine(dir, "winget.exe");
                    if (File.Exists(candidate))
                    {
                        return _cachedWingetPath = candidate;
                    }
                }
            }
        }
        catch (Exception ex) { LoggingService.LogException(ex, "InstallerService.GetWingetPath"); }

        return _cachedWingetPath = "winget";
    }
}