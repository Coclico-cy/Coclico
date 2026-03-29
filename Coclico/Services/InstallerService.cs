#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Coclico.Services;

public class InstallerService
{
    private static DateTime? _lastSourceUpdate = null;
    private static readonly SemaphoreSlim _installationLock = new SemaphoreSlim(1, 1);

    public async Task<bool> RepairWingetSourcesAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo(GetWingetPath(), "source reset --force")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var proc = Process.Start(psi);
                return proc?.WaitForExit(15000) == true && proc.ExitCode == 0;
            }
            catch { return false; }
        });
    }

    private sealed class SoftwareCategoryGroup
    {
        public string Category { get; set; } = string.Empty;
        public List<SoftwareItemDto> Items { get; set; } = [];
    }

    private sealed class SoftwareItemDto
    {
        public string Name { get; set; } = string.Empty;
        public string WingetId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CustomCommand { get; set; } = string.Empty;
    }

    public List<SoftwareItem> GetAvailableSoftware()
    {
        try
        {
            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "software_list.json");
            if (File.Exists(jsonPath))
            {
                using var fs = File.OpenRead(jsonPath);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var groups = JsonSerializer.Deserialize<List<SoftwareCategoryGroup>>(fs, opts);
                if (groups != null && groups.Count > 0)
                {
                    return groups
                        .SelectMany(g => g.Items.Select(i => new SoftwareItem
                        {
                            Name = i.Name,
                            Category = g.Category,
                            WingetId = i.WingetId,
                            CustomCommand = i.CustomCommand,
                            Status = "Prêt"
                        }))
                        .ToList();
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "InstallerService.LoadPackages");
        }

        return new List<SoftwareItem>
        {
            new SoftwareItem { Name = "Google Chrome", Category = "🌐 INTERNET", WingetId = "Google.Chrome" },
            new SoftwareItem { Name = "Mozilla Firefox", Category = "🌐 INTERNET", WingetId = "Mozilla.Firefox" },
            new SoftwareItem { Name = "Brave Browser", Category = "🌐 INTERNET", WingetId = "Brave.Brave" },
            new SoftwareItem { Name = "Microsoft Edge", Category = "🌐 INTERNET", WingetId = "Microsoft.Edge" },
            new SoftwareItem { Name = "Zen Browser", Category = "🌐 INTERNET", WingetId = "Zen-Team.Zen-Browser" },

            new SoftwareItem { Name = "Visual C++ 2015-2022 (x64)", Category = "🧩 RUNTIMES", WingetId = "Microsoft.VCRedist.2015+.x64" },
            new SoftwareItem { Name = "Visual C++ 2015-2022 (x86)", Category = "🧩 RUNTIMES", WingetId = "Microsoft.VCRedist.2015+.x86" },
            new SoftwareItem { Name = ".NET Desktop Runtime 8", Category = "🧩 RUNTIMES", WingetId = "Microsoft.DotNet.DesktopRuntime.8" },
            new SoftwareItem { Name = ".NET Desktop Runtime 6", Category = "🧩 RUNTIMES", WingetId = "Microsoft.DotNet.DesktopRuntime.6" },

            new SoftwareItem { Name = "Visual Studio 2022 Community", Category = "💻 DÉVELOPPEMENT", WingetId = "Microsoft.VisualStudio.2022.Community" },
            new SoftwareItem { Name = "Visual Studio Code", Category = "💻 DÉVELOPPEMENT", WingetId = "Microsoft.VisualStudioCode" },
            new SoftwareItem { Name = "Git", Category = "💻 DÉVELOPPEMENT", WingetId = "Git.Git" },
            new SoftwareItem { Name = "GitHub Desktop", Category = "💻 DÉVELOPPEMENT", WingetId = "GitHub.GitHubDesktop" },
            new SoftwareItem { Name = "Notepad++", Category = "💻 DÉVELOPPEMENT", WingetId = "Notepad++.Notepad++" },
            new SoftwareItem { Name = "Python 3.12", Category = "💻 DÉVELOPPEMENT", WingetId = "Python.Python.3.12" },
            new SoftwareItem { Name = "Node.js LTS", Category = "💻 DÉVELOPPEMENT", WingetId = "OpenJS.NodeJS.LTS" },
            new SoftwareItem { Name = "Docker Desktop", Category = "💻 DÉVELOPPEMENT", WingetId = "Docker.DockerDesktop" },

            new SoftwareItem { Name = "Steam", Category = "🎮 GAMING", WingetId = "Valve.Steam" },
            new SoftwareItem { Name = "Epic Games Store", Category = "🎮 GAMING", WingetId = "EpicGames.EpicGamesLauncher" },
            new SoftwareItem { Name = "Ubisoft Connect", Category = "🎮 GAMING", WingetId = "Ubisoft.Connect" },
            new SoftwareItem { Name = "EA App", Category = "🎮 GAMING", WingetId = "ElectronicArts.EADesktop" },
            new SoftwareItem { Name = "GOG Galaxy", Category = "🎮 GAMING", WingetId = "GOG.Galaxy" },

            new SoftwareItem { Name = "VLC Media Player", Category = "🎨 CRÉATION", WingetId = "VideoLAN.VLC" },
            new SoftwareItem { Name = "OBS Studio", Category = "🎨 CRÉATION", WingetId = "OBSProject.OBSStudio" },
            new SoftwareItem { Name = "Paint.NET", Category = "🎨 CRÉATION", WingetId = "dotPDN.PaintDotNet" },
            new SoftwareItem { Name = "GIMP", Category = "🎨 CRÉATION", WingetId = "GIMP.GIMP.2" },
            new SoftwareItem { Name = "Krita", Category = "🎨 CRÉATION", WingetId = "KDE.Krita" },
            new SoftwareItem { Name = "Blender", Category = "🎨 CRÉATION", WingetId = "BlenderFoundation.Blender" },
            new SoftwareItem { Name = "Audacity", Category = "🎨 CRÉATION", WingetId = "Audacity.Audacity" },
            new SoftwareItem { Name = "HandBrake", Category = "🎨 CRÉATION", WingetId = "HandBrake.HandBrake" },

            new SoftwareItem { Name = "Microsoft PowerToys", Category = "🔧 SYSTÈME", WingetId = "Microsoft.PowerToys" },
            new SoftwareItem { Name = "Revo Uninstaller", Category = "🔧 SYSTÈME", WingetId = "RevoUninstaller.RevoUninstaller" },
            new SoftwareItem { Name = "BleachBit", Category = "🔧 SYSTÈME", WingetId = "BleachBit.BleachBit" },
            new SoftwareItem { Name = "WizTree", Category = "🔧 SYSTÈME", WingetId = "AntibodySoftware.WizTree" },
            new SoftwareItem { Name = "Everything", Category = "🔧 SYSTÈME", WingetId = "voidtools.Everything" },
            new SoftwareItem { Name = "Rufus", Category = "🔧 SYSTÈME", WingetId = "Rufus.Rufus" },
            new SoftwareItem { Name = "7-Zip", Category = "🔧 SYSTÈME", WingetId = "7zip.7zip" },
            new SoftwareItem { Name = "UniGetUI", Category = "🔧 SYSTÈME", WingetId = "MartiCliment.UniGetUI" },
            new SoftwareItem { Name = "Windhawk", Category = "🔧 SYSTÈME", WingetId = "RamenSoftware.Windhawk" },

            new SoftwareItem { Name = "NVCleanstall", Category = "⚙️ HARDWARE", WingetId = "TechPowerUp.NVCleanstall" },
            new SoftwareItem { Name = "DDU (Display Driver Uninstaller)", Category = "⚙️ HARDWARE", WingetId = "Wagnardsoft.DisplayDriverUninstaller" },
            new SoftwareItem { Name = "CrystalDiskInfo", Category = "⚙️ HARDWARE", WingetId = "CrystalDewWorld.CrystalDiskInfo" },
            new SoftwareItem { Name = "CPU-Z", Category = "⚙️ HARDWARE", WingetId = "CPUID.CPU-Z" },
            new SoftwareItem { Name = "GPU-Z", Category = "⚙️ HARDWARE", WingetId = "TechPowerUp.GPU-Z" },
            new SoftwareItem { Name = "HWMonitor", Category = "⚙️ HARDWARE", WingetId = "CPUID.HWMonitor" },
            new SoftwareItem { Name = "Elgato Wave Link", Category = "⚙️ HARDWARE", WingetId = "Elgato.WaveLink" },

            new SoftwareItem { Name = "LibreOffice", Category = "📄 BUREAUTIQUE", WingetId = "TheDocumentFoundation.LibreOffice" },
            new SoftwareItem { Name = "Adobe Acrobat Reader", Category = "📄 BUREAUTIQUE", WingetId = "Adobe.Acrobat.Reader.64-bit" },
        };
    }

    public async Task<bool> InstallSoftwareAsync(SoftwareItem software, Action<string>? onOutput = null, CancellationToken ct = default)
    {
        if (_installationLock.CurrentCount == 0)
        {
            software.Status = "En attente...";
            software.AppendOutput("⏳ Une autre installation est en cours. Mise en file d'attente...");
        }

        await _installationLock.WaitAsync(ct);
        try
        {
            if (_lastSourceUpdate == null || (DateTime.Now - _lastSourceUpdate.Value).TotalMinutes > 120)
            {
                software.AppendOutput("🔄 Synchronisation avec les serveurs Winget...");
                await Task.Run(() =>
                {
                    try
                    {
                        var psi = new ProcessStartInfo(GetWingetPath(), "source update")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        Process.Start(psi)?.WaitForExit(10000);
                        _lastSourceUpdate = DateTime.Now;
                    }
                    catch { }
                }, ct);
            }

            return await RunWinget(software, onOutput, ct);
        }
        finally
        {
            _installationLock.Release();
        }
    }

    private async Task<bool> IsAlreadyInstalled(string wingetId)
    {
        if (string.IsNullOrEmpty(wingetId)) return false;
        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo(GetWingetPath(), $"list --id {wingetId}")
                {
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using var proc = Process.Start(psi);
                if (proc == null) return false;
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                if (proc.ExitCode != 0) return false;
                return !string.IsNullOrWhiteSpace(output) && output.IndexOf(wingetId, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        });
    }

    public async Task UpgradeAllAsync(Action<string>? onOutput)
    {
        onOutput?.Invoke("🚀 Démarrage de la mise à jour globale forcée (Silencieux)...");

        await _installationLock.WaitAsync();
        try
        {
            string args = "upgrade --all --force --include-unknown --silent --accept-package-agreements --accept-source-agreements";

            var info = new ProcessStartInfo
            {
                FileName = GetWingetPath(),
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var proc = new Process { StartInfo = info };
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) onOutput?.Invoke(e.Data); };
            proc.Start();
            proc.BeginOutputReadLine();
            await proc.WaitForExitAsync();

            if (proc.ExitCode == 0) onOutput?.Invoke("✅ Système mis à jour avec succès.");
            else onOutput?.Invoke($"⚠️ Fin de mise à jour (Code : {proc.ExitCode})");
        }
        catch (Exception ex)
        {
            onOutput?.Invoke($"❌ Erreur mise à jour : {ex.Message}");
        }
        finally
        {
            _installationLock.Release();
        }
    }

    private async Task<bool> RunWinget(SoftwareItem software, Action<string>? onOutput, CancellationToken ct)
    {
        string wingetPath = GetWingetPath();
        string wingetArgs;

        if (!string.IsNullOrEmpty(software.CustomCommand))
        {
            wingetArgs = software.CustomCommand;
            software.AppendOutput("🚀 Exécution d'un script personnalisé...");
        }
        else
        {
            string scope = "--scope machine";
            try { scope = ServiceContainer.GetRequired<SettingsService>().Settings.WingetScope == "user" ? "--scope user" : "--scope machine"; } catch { }
            wingetArgs = $"install --id {software.WingetId} -e --accept-package-agreements --accept-source-agreements --force {scope}";
            software.AppendOutput("📦 Lancement de l'installateur interactif...");
        }

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = wingetPath,
                Arguments = wingetArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var proc = Process.Start(info);
            if (proc == null) return false;

            software.Status = "En cours...";

            proc.OutputDataReceived += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onOutput?.Invoke(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onOutput?.Invoke($"[ERR] {e.Data}"); };

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(ct);

            software.AppendOutput("⏳ Finalisation de l'enregistrement...");
            await Task.Delay(4000, ct);

            bool finalCheck = await IsAlreadyInstalled(software.WingetId);

            if (!finalCheck)
            {
                await Task.Delay(2000, ct);
                finalCheck = await IsAlreadyInstalled(software.WingetId);
            }

            software.Status = finalCheck ? "Installé" : "Erreur";
            if (finalCheck)
            {
                software.AppendOutput("✅ Installation confirmée.");
                software.ProgressValue = 100;
            }
            else
            {
                software.AppendOutput("⚠️ L'installation a pu échouer ou nécessite un redémarrage.");
            }

            return finalCheck;
        }
        catch (Exception ex)
        {
            software.AppendOutput($"❌ Erreur système : {ex.Message}");
            return false;
        }
    }

    public bool IsRunAsAdmin()
    {
        using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private string GetWingetPath()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userPath = Path.Combine(local, @"Microsoft\WindowsApps\winget.exe");
        if (File.Exists(userPath)) return userPath;

        return "winget";
    }

    public class SoftwareItem : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string WingetId { get; set; } = "";
        public string CustomCommand { get; set; } = "";

        private string _status = "Prêt";
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

        private string _lastOutput = "";
        public string LastOutput { get => _lastOutput; set { _lastOutput = value; OnPropertyChanged(); } }

        private bool _isInstalling;
        public bool IsInstalling { get => _isInstalling; set { _isInstalling = value; OnPropertyChanged(); } }

        private double _progressValue;
        public double ProgressValue { get => _progressValue; set { _progressValue = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void AppendOutput(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var stamp = DateTime.Now.ToString("HH:mm:ss");
            if (string.IsNullOrEmpty(LastOutput))
                LastOutput = $"[{stamp}] {text}";
            else
                LastOutput = LastOutput + Environment.NewLine + $"[{stamp}] {text}";
            if (LastOutput.Length > 32_000) LastOutput = LastOutput.Substring(LastOutput.Length - 32_000);
        }
    }
}
