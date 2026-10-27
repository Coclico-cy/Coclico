using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Installer.Models;
using Microsoft.Win32;

namespace Coclico.Installer.Services;

public record InstallProgressReport(double Percent, string Status, string SubStatus);

public static class InstallService
{
    private const string AppName = "Coclico";
    public static readonly string AppVersion =
        typeof(InstallService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "0.0.0";
    private const string AppPublisher = "Coclico";
    private const string AppUrl = "https://github.com/Coclico-cy/Coclico";
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Coclico";

    private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, uint dwFlags);

    public static bool IsCoclicoRunning()
    {
        return Process.GetProcessesByName("Coclico").Length > 0;
    }

    public static void CloseRunningCoclico()
    {
        foreach (var process in Process.GetProcessesByName("Coclico"))
        {
            bool closed = false;
            try
            {
                if (!process.HasExited)
                {
                    _ = process.CloseMainWindow();
                }

                closed = process.WaitForExit(5000);
                if (!closed)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception killEx) when (killEx is not System.OutOfMemoryException)
                    {
                        Debug.WriteLine($"[InstallService] Arrêt forcé du processus Coclico échoué : {killEx.Message}");
                    }

                    closed = process.WaitForExit(5000);
                }
            }
            catch (Exception ex) when (ex is not System.OutOfMemoryException)
            {
                Debug.WriteLine($"[InstallService] Fermeture du processus Coclico échouée : {ex.Message}");
            }

            if (!closed)
            {
                throw new InvalidOperationException("Coclico n'a pas pu être fermé, ferme-le manuellement puis relance l'installation.");
            }
        }
    }

    public static async Task InstallAsync(InstallConfig config, IProgress<InstallProgressReport> progress, CancellationToken ct = default)
    {
        await Task.Run(async () =>
        {
            progress.Report(new InstallProgressReport(5, "Préparation de l'environnement...", "Vérification des processus actifs"));

            if (IsCoclicoRunning())
            {
                progress.Report(new InstallProgressReport(8, "Fermeture de Coclico...", "Arrêt du processus en cours"));
                CloseRunningCoclico();
                Thread.Sleep(500);
            }

            progress.Report(new InstallProgressReport(10, "Vérification du runtime .NET 10...", ""));
            if (IsDesktopRuntime10Installed())
            {
                progress.Report(new InstallProgressReport(14, "Runtime .NET 10 déjà installé.", "Étape ignorée."));
            }
            else
            {
                var runtimeProgress = new Progress<InstallProgressReport>(runtimeReport =>
                {
                    progress.Report(new InstallProgressReport(10 + (runtimeReport.Percent * 0.04), runtimeReport.Status, runtimeReport.SubStatus));
                });
                await DotnetRuntimeService.EnsureRuntimeAsync(runtimeProgress, ct);
            }

            string installFullPath = Path.GetFullPath(config.InstallPath);
            string installParent = Path.GetDirectoryName(installFullPath) ?? Path.GetTempPath();
            string stagingDir = Path.Join(installParent, ".coclico-stage-" + Guid.NewGuid().ToString("N"));
            string? backupDir = null;
            bool switched = false;

            try
            {
                Directory.CreateDirectory(stagingDir);

                progress.Report(new InstallProgressReport(12, "Recherche des composants d'installation...", ""));
                Stream? zipStream = GetPayloadStream();

                if (zipStream != null)
                {
                    using (zipStream)
                    {
                        progress.Report(new InstallProgressReport(15, "Extraction sécurisée du payload...", "Chaque entrée de l'archive est validée avant écriture"));
                        PayloadGuard.ExtractSafely(zipStream, stagingDir, (extracted, total) =>
                        {
                            ct.ThrowIfCancellationRequested();
                            double percent = 15 + ((double)extracted / Math.Max(1, total) * 50);
                            progress.Report(new InstallProgressReport(percent, $"Extraction : {extracted}/{total} entrées", ""));
                        });
                    }
                }
                else
                {
                    string devPublishPath = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "publish");
                    if (Directory.Exists(devPublishPath))
                    {
                        CopyDirectoryWithProgress(devPublishPath, stagingDir, progress, ct);
                    }
                    else
                    {
                        throw new FileNotFoundException("Archive d'installation introuvable (payload.zip absent).");
                    }
                }

                progress.Report(new InstallProgressReport(67, "Vérification cryptographique du payload...", "Manifeste d'intégrité signé"));
                PayloadVerification verification = PayloadGuard.VerifyStaging(stagingDir);
                if (verification.HasManifest && !verification.IsVerified)
                {
                    throw new InvalidOperationException("Payload rejeté : " + verification.Reason);
                }

                if (!verification.HasManifest)
                {
                    progress.Report(new InstallProgressReport(70, "Aucun manifeste d'intégrité dans le payload.", verification.Reason));
                }

                progress.Report(new InstallProgressReport(71, "Bascule contrôlée de l'installation...", "L'ancienne version est conservée jusqu'à validation"));
                try
                {
                    if (Directory.Exists(installFullPath))
                    {
                        backupDir = installFullPath + ".backup-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                        MoveWithRetry(installFullPath, backupDir);
                    }

                    MoveWithRetry(stagingDir, installFullPath);
                    switched = true;
                }
                catch (Exception switchEx) when (switchEx is IOException or UnauthorizedAccessException)
                {
                    TryRestoreBackup(backupDir, installFullPath);
                    throw new InvalidOperationException("Bascule impossible, l'installation existante a été restaurée : " + switchEx.Message, switchEx);
                }

                progress.Report(new InstallProgressReport(74, "Contrôle final des fichiers installés...", verification.Reason));
                PayloadVerification finalCheck = PayloadGuard.VerifyStaging(installFullPath);
                if (finalCheck.HasManifest && !finalCheck.IsVerified)
                {
                    RollbackSwitch(installFullPath, backupDir);
                    throw new InvalidOperationException("Contrôle final échoué, retour à la version précédente : " + finalCheck.Reason);
                }

                if (backupDir != null)
                {
                    TryDeleteDirectory(backupDir);
                    backupDir = null;
                }

                progress.Report(new InstallProgressReport(75, "Application des permissions Windows...", ""));
                if (!PayloadGuard.TryHardenInstallAcl(installFullPath))
                {
                    Debug.WriteLine("[InstallService] Durcissement ACL non appliqué (non bloquant).");
                }
            }
            finally
            {
                TryDeleteDirectory(stagingDir);
                if (switched && backupDir != null)
                {
                    TryDeleteDirectory(backupDir);
                }
            }

            if (config.InstallCuda)
            {
                progress.Report(new InstallProgressReport(76, "Téléchargement du runtime NVIDIA CUDA 12 (~1,2 Go)...", "Connexion à nuget.org..."));
                try
                {
                    var cudaProgress = new Progress<InstallProgressReport>(cudaReport =>
                    {
                        progress.Report(new InstallProgressReport(76 + (cudaReport.Percent * 0.05), cudaReport.Status, cudaReport.SubStatus));
                    });
                    await CudaRuntimeDownloader.DownloadAsync(config.InstallPath, cudaProgress, ct);
                    progress.Report(new InstallProgressReport(81, "Runtime NVIDIA CUDA 12 installé avec succès.", ""));
                }
                catch (Exception cudaEx) when (cudaEx is not System.OutOfMemoryException)
                {
                    progress.Report(new InstallProgressReport(81, "Téléchargement CUDA ignoré : " + cudaEx.Message, "L'IA locale fonctionnera en mode CPU."));
                }
            }

            if (config.InstallAiModel && config.SelectedAiModel != null)
            {
                progress.Report(new InstallProgressReport(82, $"Téléchargement du modèle IA ({config.SelectedAiModel.DisplayName})...", "Connexion HuggingFace..."));
                try
                {
                    var aiProgress = new Progress<InstallProgressReport>(aiReport =>
                    {
                        double globalPercent = 82 + (aiReport.Percent * 0.13);
                        progress.Report(new InstallProgressReport(globalPercent, aiReport.Status, aiReport.SubStatus));
                    });
                    await AiModelDownloader.DownloadModelAsync(config.InstallPath, config.SelectedAiModel, aiProgress, ct);
                }
                catch (Exception ex) when (ex is not System.OutOfMemoryException)
                {
                    progress.Report(new InstallProgressReport(85, "Téléchargement IA ignoré : " + ex.Message, ""));
                }
            }

            ConfigureCoclicoSettings(config);

            progress.Report(new InstallProgressReport(88, "Configuration du gestionnaire de désinstallation...", ""));
            string currentExe = Environment.ProcessPath ?? string.Empty;

            string exePath = Path.Join(config.InstallPath, "Coclico.exe");

            progress.Report(new InstallProgressReport(90, "Création des raccourcis...", ""));
            if (config.CreateDesktopShortcut)
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                ShortcutHelper.CreateShortcut(exePath, Path.Join(desktop, "Coclico.lnk"), "Coclico - Assistant Système", exePath);
            }

            if (config.CreateStartMenuShortcut)
            {
                string startMenu = Path.Join(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "Programs", "Coclico");
                ShortcutHelper.CreateShortcut(exePath, Path.Join(startMenu, "Coclico.lnk"), "Coclico - Assistant Système", exePath);
            }

            progress.Report(new InstallProgressReport(92, "Préparation de la désinstallation...", ""));
            string uninstallerDir = Path.Join(config.InstallPath, "Uninstall");
            string uninstallerPath = Path.Join(uninstallerDir, "Coclico.Installer.exe");

            try
            {
                Directory.CreateDirectory(uninstallerDir);
                if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
                {
                    File.Copy(currentExe, uninstallerPath, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
            {
                Debug.WriteLine($"Erreur préparation désinstallation: {ex.Message}");
                uninstallerPath = currentExe;
            }

            progress.Report(new InstallProgressReport(96, "Enregistrement dans Windows...", "Ajout dans Applications et fonctionnalités"));
            RegisterUninstall(config.InstallPath, uninstallerPath, exePath);

            progress.Report(new InstallProgressReport(100, "Installation terminée avec succès !", "Coclico est prêt à être utilisé."));
        }, ct);
    }

    public static void ConfigureCoclicoSettings(InstallConfig config)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string coclicoDir = Path.Join(appData, "Coclico");
            Directory.CreateDirectory(coclicoDir);
            string settingsFile = Path.Join(coclicoDir, "settings.json");
            JsonNode? root = null;
            try
            {
                string existingJson = File.ReadAllText(settingsFile);
                root = JsonNode.Parse(existingJson);
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Text.Json.JsonException or System.FormatException)
            {
                Debug.WriteLine($"[InstallService] Lecture des réglages existants impossible : {ex.Message}");
            }

            root ??= new JsonObject();
            if (root is not JsonObject obj)
            {
                root = obj = new JsonObject();
            }

            if (config.InstallAiModel && config.SelectedAiModel != null)
            {
                obj["aiProvider"] = "LocalGGUF";
                obj["aiModel"] = config.SelectedAiModel.Id;
            }

            if (config.InstallCuda)
            {
                obj["aiUseGpu"] = true;
            }

            File.WriteAllText(settingsFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Text.Json.JsonException or System.FormatException)
        {
            Debug.WriteLine($"[InstallService] Écriture des réglages impossible : {ex.Message}");
        }
    }

    public static async Task UninstallAsync(IProgress<InstallProgressReport> progress, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            string installPath = Path.GetDirectoryName(Environment.ProcessPath) ??
                Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Coclico");

            progress.Report(new InstallProgressReport(10, "Fermeture des processus actifs...", ""));
            if (IsCoclicoRunning())
            {
                CloseRunningCoclico();
                Thread.Sleep(500);
            }

            progress.Report(new InstallProgressReport(30, "Suppression des raccourcis...", ""));
            string desktopShortcut = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Coclico.lnk");
            ShortcutHelper.DeleteShortcut(desktopShortcut);

            string startMenuDir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Coclico");
            if (Directory.Exists(startMenuDir))
            {
                try { Directory.Delete(startMenuDir, recursive: true); } catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { Debug.WriteLine($"[InstallService] Suppression du dossier du menu Démarrer échouée : {ex.Message}"); }
            }

            progress.Report(new InstallProgressReport(55, "Nettoyage du Registre Windows...", ""));
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(RegistryKeyPath, throwOnMissingSubKey: false);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or System.IO.IOException or System.UnauthorizedAccessException)
            {
                Debug.WriteLine($"[InstallService] Nettoyage de la clé de registre échoué : {ex.Message}");
            }

            progress.Report(new InstallProgressReport(75, "Suppression des fichiers...", ""));
            if (Directory.Exists(installPath))
            {
                TryDeleteDirectoryContents(installPath);
            }

            progress.Report(new InstallProgressReport(95, "Finalisation de la désinstallation...", ""));
            ScheduleSelfDelete(installPath);

            progress.Report(new InstallProgressReport(100, "Coclico a été désinstallé.", "Merci d'avoir utilisé Coclico !"));
        }, ct);
    }

    public static async Task FullUninstallAsync(IProgress<InstallProgressReport> progress, string? installPath = null, bool keepUserData = false, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            string resolvedPath = ResolveInstallPath(installPath);

            progress.Report(new InstallProgressReport(5, "Fermeture des processus actifs...", "Arrêt des applications Coclico"));
            if (IsCoclicoRunning())
            {
                CloseRunningCoclico();
                Thread.Sleep(500);
            }

            progress.Report(new InstallProgressReport(10, "Suppression des raccourcis Bureau et Menu Demarrer...", ""));
            string desktopShortcut = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Coclico.lnk");
            ShortcutHelper.DeleteShortcut(desktopShortcut);

            string startMenuDir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Coclico");
            if (Directory.Exists(startMenuDir))
            {
                try { Directory.Delete(startMenuDir, recursive: true); } catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { Debug.WriteLine($"[InstallService] Suppression du dossier du menu Démarrer échouée : {ex.Message}"); }
            }

            progress.Report(new InstallProgressReport(25,
                keepUserData ? "Conservation des données utilisateur (%AppData%)..." : "Suppression des donnees de configuration (%AppData%)...",
                ""));
            if (!keepUserData)
            {
                string appDataPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico");
                if (Directory.Exists(appDataPath))
                {
                    try { Directory.Delete(appDataPath, recursive: true); } catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { Debug.WriteLine($"[InstallService] Suppression du dossier AppData échouée : {ex.Message}"); }
                }

                string localAppDataPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Coclico");
                if (Directory.Exists(localAppDataPath))
                {
                    try { Directory.Delete(localAppDataPath, recursive: true); } catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { Debug.WriteLine($"[InstallService] Suppression du dossier LocalAppData échouée : {ex.Message}"); }
                }
            }

            progress.Report(new InstallProgressReport(40, "Nettoyage des fichiers temporaires...", ""));
            string tempPath = Path.Join(Path.GetTempPath(), "Coclico");
            if (Directory.Exists(tempPath))
            {
                try { Directory.Delete(tempPath, recursive: true); } catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { Debug.WriteLine($"[InstallService] Suppression du dossier temporaire échouée : {ex.Message}"); }
            }

            progress.Report(new InstallProgressReport(55, "Nettoyage du Registre Windows...", ""));
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(RegistryKeyPath, throwOnMissingSubKey: false);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or System.IO.IOException or System.UnauthorizedAccessException)
            {
                Debug.WriteLine($"[InstallService] Nettoyage de la clé de registre échoué : {ex.Message}");
            }

            progress.Report(new InstallProgressReport(70, "Suppression du dossier d'installation...", resolvedPath));
            if (!string.IsNullOrEmpty(resolvedPath) && Directory.Exists(resolvedPath))
            {
                try
                {
                    Directory.Delete(resolvedPath, recursive: true);
                }
                catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
                {
                    Debug.WriteLine($"[InstallService] Suppression du dossier d'installation échouée : {ex.Message}");
                    TryDeleteDirectoryContents(resolvedPath);
                    try { Directory.Delete(resolvedPath, recursive: true); } catch (Exception ex2) when (ex2 is System.IO.IOException or System.UnauthorizedAccessException) { Debug.WriteLine($"[InstallService] Nouvelle tentative de suppression échouée : {ex2.Message}"); }
                }
            }

            progress.Report(new InstallProgressReport(90, "Finalisation de la désinstallation complète...", ""));

            if (!string.IsNullOrEmpty(resolvedPath) && Directory.Exists(resolvedPath))
            {
                ScheduleSelfDelete(resolvedPath);
            }

            Thread.Sleep(500);

            progress.Report(new InstallProgressReport(100, "Désinstallation complète !", "Tous les fichiers, données et entrées de registre ont été supprimés."));
        }, ct);
    }

    private static bool IsDesktopRuntime10Installed()
    {
        try
        {
            string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (string.IsNullOrWhiteSpace(dotnetRoot) || !Directory.Exists(dotnetRoot))
            {
                dotnetRoot = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
            }

            string sharedDir = Path.Join(dotnetRoot, "shared", "Microsoft.WindowsDesktop.App");
            if (!Directory.Exists(sharedDir))
            {
                return false;
            }

            foreach (string dir in Directory.EnumerateDirectories(sharedDir))
            {
                if (Path.GetFileName(dir).StartsWith("10.", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            Debug.WriteLine($"[InstallService] Détection du runtime .NET partagé impossible : {ex.Message}");
        }

        return false;
    }

    private static void TryDeleteDirectoryContents(string path)
    {
        foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).Equals("Uninstall.exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try { File.Delete(file); } catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { Debug.WriteLine($"[InstallService] Suppression du fichier '{file}' échouée : {ex.Message}"); }
        }

        foreach (string dir in Directory.GetDirectories(path, "*", SearchOption.AllDirectories))
        {
            try { Directory.Delete(dir, recursive: true); } catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { Debug.WriteLine($"[InstallService] Suppression du dossier '{dir}' échouée : {ex.Message}"); }
        }
    }

    private static string ResolveInstallPath(string? installPath)
    {
        if (!string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath))
        {
            return installPath;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            var installLocation = key?.GetValue("InstallLocation") as string;
            if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
            {
                return installLocation;
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Security.SecurityException)
        {
            Debug.WriteLine($"[InstallService] Lecture de l'emplacement d'installation dans le registre échouée : {ex.Message}");
        }

        string processDir = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;

        if (!string.IsNullOrEmpty(processDir) &&
            Path.GetFileName(processDir.TrimEnd(Path.DirectorySeparatorChar))
                .Equals("Uninstall", StringComparison.OrdinalIgnoreCase))
        {
            string? parentDir = Path.GetDirectoryName(processDir.TrimEnd(Path.DirectorySeparatorChar));
            if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
            {
                return parentDir;
            }
        }

        if (!string.IsNullOrEmpty(processDir) && Directory.Exists(processDir))
        {
            return processDir;
        }

        string localAppDataPath = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Coclico");
        if (Directory.Exists(localAppDataPath))
        {
            return localAppDataPath;
        }

        return string.Empty;
    }

    private static Stream? GetPayloadStream()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string resourceName = "Coclico.Installer.payload.zip";
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream != null && stream.Length > 100)
        {
            return stream;
        }
        stream?.Dispose();

        string localZip = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "payload.zip");
        if (File.Exists(localZip) && new FileInfo(localZip).Length > 100)
        {
            return File.OpenRead(localZip);
        }

        string artifactZip = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "payload.zip");
        if (File.Exists(artifactZip) && new FileInfo(artifactZip).Length > 100)
        {
            return File.OpenRead(artifactZip);
        }

        return null;
    }

    private static void CopyDirectoryWithProgress(string sourceDir, string targetDir, IProgress<InstallProgressReport> progress, CancellationToken ct)
    {
        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        int total = files.Length;
        int current = 0;

        foreach (string file in files)
        {
            ct.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(sourceDir, file);
            string dest = Path.Join(targetDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);

            current++;
            double percent = 15 + ((double)current / total * 70);
            progress.Report(new InstallProgressReport(percent, $"Copie : {Path.GetFileName(file)}", $"{current}/{total}"));
        }
    }

    private static void RegisterUninstall(string installPath, string uninstallerPath, string mainExePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            if (key == null) return;

            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", AppVersion);
            key.SetValue("Publisher", AppPublisher);
            key.SetValue("DisplayIcon", $"{mainExePath},0");
            key.SetValue("InstallLocation", installPath);
            key.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" --uninstall --silent");
            key.SetValue("URLInfoAbout", AppUrl);
            key.SetValue("HelpLink", $"{AppUrl}/issues");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

            try
            {
                long totalBytes = 0;
                foreach (string f in Directory.EnumerateFiles(installPath, "*", SearchOption.AllDirectories))
                {
                    totalBytes += new FileInfo(f).Length;
                }
                key.SetValue("EstimatedSize", (int)(totalBytes / 1024), RegistryValueKind.DWord);
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
            {
                Debug.WriteLine($"[InstallService] Calcul de la taille installée impossible : {ex.Message}");
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Security.SecurityException)
        {
            Debug.WriteLine($"Erreur inscription registre: {ex.Message}");
        }
    }

    private static void ScheduleSelfDelete(string installPath)
    {
        TryScheduleDeleteOnReboot(Environment.ProcessPath ?? string.Empty);
        TryScheduleDeleteOnReboot(Path.Join(installPath, "CoclicoUninstaller.exe"));

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c timeout /t 12 /nobreak > NUL & if not exist \"{installPath}\\Coclico.exe\" rmdir /s /q \"{installPath}\" & timeout /t 8 /nobreak > NUL & if not exist \"{installPath}\\Coclico.exe\" rmdir /s /q \"{installPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.InvalidOperationException)
        {
            Debug.WriteLine($"[InstallService] Planification de la suppression différée impossible : {ex.Message}");
        }
    }

    private static bool TryScheduleDeleteOnReboot(string filePath)
    {
        try
        {
            return File.Exists(filePath) && MoveFileEx(filePath, null, MOVEFILE_DELAY_UNTIL_REBOOT);
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            Debug.WriteLine($"[InstallService] Planification de la suppression au redémarrage impossible pour '{filePath}' : {ex.Message}");
            return false;
        }
    }

    private static void MoveWithRetry(string sourceDir, string destDir)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Move(sourceDir, destDir);
                return;
            }
            catch (Exception ex) when (attempt < 4 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(1000);
            }
        }
    }

    private static void TryRestoreBackup(string? backupDir, string installPath)
    {
        try
        {
            if (backupDir != null && Directory.Exists(backupDir) && !Directory.Exists(installPath))
            {
                Directory.Move(backupDir, installPath);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            Debug.WriteLine($"[InstallService] Restauration de secours impossible : {ex.Message}");
        }
    }

    private static void RollbackSwitch(string installPath, string? backupDir)
    {
        try
        {
            if (Directory.Exists(installPath))
            {
                string failedDir = installPath + ".failed-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                Directory.Move(installPath, failedDir);
            }

            if (backupDir != null && Directory.Exists(backupDir))
            {
                Directory.Move(backupDir, installPath);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            Debug.WriteLine($"[InstallService] Retour arrière impossible : {ex.Message}");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            Debug.WriteLine($"[InstallService] Nettoyage de '{path}' impossible : {ex.Message}");
        }
    }
}
