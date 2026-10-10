using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Coclico.Installer.Services;

public static class DotnetRuntimeService
{
    private const string DesktopRuntimeUrl = "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.12/windowsdesktop-runtime-10.0.12-win-x64.exe";

    public static bool IsDesktopRuntimeInstalled()
    {
        string programFilesDotnet = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");

        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (string.IsNullOrWhiteSpace(dotnetRoot))
        {
            dotnetRoot = programFilesDotnet;
        }

        string[] roots = { dotnetRoot, programFilesDotnet };

        foreach (string root in roots)
        {
            string sharedDir = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App");
            if (!Directory.Exists(sharedDir))
            {
                continue;
            }

            foreach (string versionDir in Directory.GetDirectories(sharedDir))
            {
                string versionName = Path.GetFileName(versionDir);
                if (Version.TryParse(versionName, out Version? version) && version.Major >= 10)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static async Task EnsureRuntimeAsync(IProgress<InstallProgressReport> progress, CancellationToken ct)
    {
        if (IsDesktopRuntimeInstalled())
        {
            progress.Report(new InstallProgressReport(100, "Runtime .NET 10 déjà présent.", ""));
            return;
        }

        progress.Report(new InstallProgressReport(5, "Runtime .NET 10 absent, téléchargement...", ""));

        string tempDir = Path.Combine(Path.GetTempPath(), "Coclico");
        Directory.CreateDirectory(tempDir);

        foreach (string oldFile in Directory.GetFiles(tempDir, "windowsdesktop-runtime-*.exe"))
        {
            try
            {
                File.Delete(oldFile);
            }
            catch
            {
            }
        }

        string installerPath = Path.Combine(tempDir, $"windowsdesktop-runtime-{Guid.NewGuid():N}.exe");
        string logPath = Path.Combine(tempDir, $"dotnet-runtime-install-{Guid.NewGuid():N}.log");

        try
        {
            await DownloadInstallerAsync(installerPath, progress, ct);

            progress.Report(new InstallProgressReport(80, "Installation du runtime .NET 10...", "Une fenêtre Windows va s'afficher, patientez quelques minutes"));
            await RunInstallerAsync(installerPath, logPath, progress, ct);

            progress.Report(new InstallProgressReport(100, "Runtime .NET 10 installé avec succès.", ""));
        }
        finally
        {
            try
            {
                if (File.Exists(installerPath))
                {
                    File.Delete(installerPath);
                }
            }
            catch
            {
            }
        }
    }

    private static async Task DownloadInstallerAsync(string destinationPath, IProgress<InstallProgressReport> progress, CancellationToken ct)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15)
        };

        using var response = await client.GetAsync(
            DesktopRuntimeUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(destinationPath);

        var buffer = new byte[1024 * 1024];
        long downloadedBytes = 0;
        int bytesRead;

        while ((bytesRead = await source.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloadedBytes += bytesRead;

            if (totalBytes > 0)
            {
                double percent = (double)downloadedBytes / totalBytes.Value * 100.0;
                double downloadedMb = downloadedBytes / (1024.0 * 1024.0);
                double totalMb = totalBytes.Value / (1024.0 * 1024.0);
                progress.Report(new InstallProgressReport(percent, $"Téléchargement du runtime .NET 10 : {downloadedMb:F1} / {totalMb:F1} Mo", ""));
            }
        }
    }

    private static async Task RunInstallerAsync(string installerPath, string logPath, IProgress<InstallProgressReport> progress, CancellationToken ct)
    {
        const int msiAlreadyRunning = 1618;
        const int maxAttempts = 6;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            int exitCode = await RunInstallerOnceAsync(installerPath, logPath, ct);

            if (exitCode == msiAlreadyRunning)
            {
                if (IsDesktopRuntimeInstalled())
                {
                    return;
                }

                if (attempt < maxAttempts)
                {
                    progress.Report(new InstallProgressReport(80,
                        $"Une autre installation Windows est en cours, nouvelle tentative dans 30 secondes... (tentative {attempt + 1}/{maxAttempts})", ""));
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    continue;
                }
            }

            if (exitCode != 0 && exitCode != 3010 && exitCode != 1638 && exitCode != msiAlreadyRunning)
            {
                throw new InvalidOperationException($"Échec de l'installation du runtime .NET 10 (code {exitCode}). Log : {logPath}");
            }

            if (exitCode == msiAlreadyRunning)
            {
                throw new InvalidOperationException("Une autre installation Windows est en cours. Relancez l'installation de Coclico dans quelques minutes. Log : " + logPath);
            }

            if (!IsDesktopRuntimeInstalled())
            {
                throw new InvalidOperationException($"Le runtime .NET 10 reste absent après l'installation. Log : {logPath}");
            }

            return;
        }
    }

    private static async Task<int> RunInstallerOnceAsync(string installerPath, string logPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = $"/install /passive /norestart /log \"{logPath}\"",
            UseShellExecute = true
        };

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Impossible de lancer l'installeur du runtime .NET 10 : {ex.Message}");
        }

        if (process == null)
        {
            throw new InvalidOperationException("Impossible de lancer l'installeur du runtime .NET 10.");
        }

        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }
}