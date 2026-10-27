using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Coclico.Services;

namespace Coclico.Modules.Apps.Services;

public sealed class WingetEngineService
{
    private const string BundleUrl = "https://github.com/microsoft/winget-cli/releases/download/v1.10.340/Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle";
    private const string DepsZipUrl = "https://github.com/microsoft/winget-cli/releases/download/v1.10.340/DesktopAppInstaller_Dependencies.zip";
    private const string BundleSha256 = "0388BD9803ED1934CCDE396EBFC0CBECDCAC92BD9DC0DF70F736B7D0C34C8428";

    private const int ExitAlreadyUpToDate = -1978335212;
    private const int ExitAlreadyInstalled = -1978335215;
    private const int ExitAdminRequired = -1978335189;
    private const int ExitInstallerBusy = -1978334974;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    private static readonly Regex MultiSpaceSplitRegex = new(@"\s{2,}", RegexOptions.Compiled);
    private static readonly Regex PackageIdRegex = new("^[A-Za-z0-9][A-Za-z0-9._-]{1,127}$", RegexOptions.Compiled);

    public static bool IsValidPackageId(string packageId)
    {
        return !string.IsNullOrWhiteSpace(packageId) && PackageIdRegex.IsMatch(packageId.Trim());
    }

    private readonly IProcessExecutionService _processExecution;
    private string? _cachedWingetPath;

    public enum InstallStatus
    {
        Succeeded,
        RestartRequired,
        AlreadyUpToDate,
        AlreadyInstalled,
        AdminRequired,
        Failed,
        Busy
    }

    public sealed record InstallResult(InstallStatus Status, int ExitCode);

    public sealed record WingetRow(string Name, string Id, string Version, string AvailableVersion);

    public WingetEngineService(IProcessExecutionService processExecution)
    {
        _processExecution = processExecution ?? throw new ArgumentNullException(nameof(processExecution));
    }

    private static IEnumerable<string> GetWingetCandidates()
    {
        yield return "winget";

        string? direct = null;
        try
        {
            direct = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WindowsApps\winget.exe");
        }
        catch (Exception)
        {
        }

        if (!string.IsNullOrEmpty(direct) && File.Exists(direct))
        {
            yield return direct;
        }
    }

    public async Task<string?> GetWingetPathAsync()
    {
        if (_cachedWingetPath != null)
        {
            return _cachedWingetPath;
        }

        foreach (string candidate in GetWingetCandidates())
        {
            try
            {
                ProcessExecutionResult result = await _processExecution.ExecuteAsync(
                    candidate,
                    ["--version"],
                    timeout: TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                if (result is { Success: true, ExitCode: 0 })
                {
                    _cachedWingetPath = candidate;
                    return candidate;
                }
            }
            catch (Exception)
            {
            }
        }

        return null;
    }

    public async Task<bool> IsWingetAvailableAsync()
    {
        return await GetWingetPathAsync().ConfigureAwait(false) != null;
    }

    public async Task<bool> EnsureWingetAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        if (await IsWingetAvailableAsync().ConfigureAwait(false))
        {
            return true;
        }

        log?.Report("WinGet absent : installation du bundle officiel App Installer...");
        string dir = Path.Combine(Path.GetTempPath(), "Coclico-WingetSetup");
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception)
        {
        }

        Directory.CreateDirectory(dir);

        string bundlePath = Path.Combine(dir, "Microsoft.DesktopAppInstaller.msixbundle");
        await DownloadAsync(BundleUrl, bundlePath, ct).ConfigureAwait(false);
        if (!await VerifyBundleSha256Async(bundlePath, log, ct).ConfigureAwait(false))
        {
            log?.Report("Empreinte SHA-256 du bundle winget invalide - installation abandonnée.");
            return false;
        }

        _cachedWingetPath = null;
        if (await AddAppxAsync(bundlePath, log, ct).ConfigureAwait(false)
            && await IsWingetAvailableAsync().ConfigureAwait(false))
        {
            log?.Report("WinGet installé avec succès.");
            return true;
        }

        log?.Report("Installation des dépendances officielles (VCLibs, UI.Xaml)...");
        string depsZipPath = Path.Combine(dir, "DesktopAppInstaller_Dependencies.zip");
        await DownloadAsync(DepsZipUrl, depsZipPath, ct).ConfigureAwait(false);
        List<string> dependencyPaths = ExtractX64Dependencies(depsZipPath, dir);
        foreach (string dependency in dependencyPaths)
        {
            _ = await AddAppxAsync(dependency, log, ct).ConfigureAwait(false);
        }

        string dependencyPathsArg = string.Join(",", dependencyPaths.Select(static p => $"\"{p}\""));
        _cachedWingetPath = null;
        if (await AddAppxAsync(bundlePath, log, ct,
                "-ForceUpdateFromAnyVersion",
                "-ForceApplicationShutdown",
                $"-DependencyPath {dependencyPathsArg}").ConfigureAwait(false)
            && await IsWingetAvailableAsync().ConfigureAwait(false))
        {
            log?.Report("WinGet installé avec succès.");
            return true;
        }

        log?.Report("Réparation de l'App Installer (réenregistrement)...");
        if (await RepairAppInstallerAsync(log, ct).ConfigureAwait(false))
        {
            _cachedWingetPath = null;
            if (await IsWingetAvailableAsync().ConfigureAwait(false))
            {
                log?.Report("App Installer réparé, WinGet opérationnel.");
                return true;
            }
        }

        return false;
    }

    public async Task<InstallResult> InstallAsync(string packageId, IProgress<string>? log = null, CancellationToken ct = default)
    {
        string winget = await GetWingetPathAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("WinGet n'est pas disponible.");
        log?.Report($"Installation : {packageId}");

        List<string> args = ["install", "--id", packageId, "--exact", "--source", "winget", "--silent",
            "--disable-interactivity", "--accept-package-agreements", "--accept-source-agreements", "--no-upgrade"];

        ProcessExecutionResult result = await _processExecution.ExecuteAsync(winget, args,
            timeout: TimeSpan.FromMinutes(10), ct: ct).ConfigureAwait(false);

        if (result.ExitCode == ExitInstallerBusy)
        {
            log?.Report($"{packageId} : une autre installation occupe Windows Installer. Nouvel essai dans 12 secondes...");
            await Task.Delay(TimeSpan.FromSeconds(12), ct).ConfigureAwait(false);
            result = await _processExecution.ExecuteAsync(winget, args,
                timeout: TimeSpan.FromMinutes(10), ct: ct).ConfigureAwait(false);
        }

        if (result.ExitCode == ExitInstallerBusy)
        {
            log?.Report($"{packageId} : Windows Installer toujours occupé, abandon.");
            return new InstallResult(InstallStatus.Busy, result.ExitCode);
        }

        return TranslateExitCode(result.ExitCode);
    }

    public async Task<InstallResult> UninstallAsync(string packageId, IProgress<string>? log = null, CancellationToken ct = default)
    {
        string winget = await GetWingetPathAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("WinGet n'est pas disponible.");
        log?.Report($"Désinstallation : {packageId}");

        ProcessExecutionResult result = await _processExecution.ExecuteAsync(winget,
            ["uninstall", "--id", packageId, "--exact", "--silent", "--disable-interactivity",
             "--accept-source-agreements"],
            timeout: TimeSpan.FromMinutes(10), ct: ct).ConfigureAwait(false);

        return TranslateExitCode(result.ExitCode);
    }

    private static InstallResult TranslateExitCode(int exitCode)
    {
        return exitCode switch
        {
            0 => new InstallResult(InstallStatus.Succeeded, exitCode),
            3010 or 1641 => new InstallResult(InstallStatus.RestartRequired, exitCode),
            ExitAlreadyUpToDate => new InstallResult(InstallStatus.AlreadyUpToDate, exitCode),
            ExitAlreadyInstalled => new InstallResult(InstallStatus.AlreadyInstalled, exitCode),
            ExitAdminRequired => new InstallResult(InstallStatus.AdminRequired, exitCode),
            _ => new InstallResult(InstallStatus.Failed, exitCode)
        };
    }

    public async Task<HashSet<string>> DetectInstalledIdsAsync(CancellationToken ct = default)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? winget = await GetWingetPathAsync().ConfigureAwait(false);
        if (winget == null)
        {
            return ids;
        }

        try
        {
            ProcessExecutionResult result = await _processExecution.ExecuteAsync(winget,
                ["list", "--source", "winget", "--accept-source-agreements"],
                timeout: TimeSpan.FromMinutes(2), ct: ct).ConfigureAwait(false);

            foreach (string rawLine in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                ct.ThrowIfCancellationRequested();
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("---"))
                {
                    continue;
                }

                if (line.StartsWith("Nom", StringComparison.Ordinal) || line.StartsWith("Name", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = MultiSpaceSplitRegex.Split(line);
                if (parts.Length >= 2)
                {
                    _ = ids.Add(parts[1].Trim());
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "WingetEngineService.DetectInstalledIdsAsync");
        }

        return ids;
    }

    public async Task<List<WingetRow>> GetAvailableUpgradesAsync(CancellationToken ct = default)
    {
        var upgrades = new List<WingetRow>();
        string? winget = await GetWingetPathAsync().ConfigureAwait(false);
        if (winget == null)
        {
            return upgrades;
        }

        try
        {
            ProcessExecutionResult result = await _processExecution.ExecuteAsync(winget,
                ["upgrade", "--include-unknown", "--accept-source-agreements"],
                timeout: TimeSpan.FromMinutes(2), ct: ct).ConfigureAwait(false);

            bool headerPassed = false;
            foreach (string rawLine in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                ct.ThrowIfCancellationRequested();
                string line = rawLine.Trim();
                if (line.StartsWith("---"))
                {
                    headerPassed = true;
                    continue;
                }

                if (!headerPassed || line.Length == 0)
                {
                    continue;
                }

                string[] parts = MultiSpaceSplitRegex.Split(line);
                if (parts.Length >= 3)
                {
                    upgrades.Add(new WingetRow(parts[0].Trim(), parts[1].Trim(), parts[2].Trim(),
                        parts.Length > 3 ? parts[3].Trim() : string.Empty));
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "WingetEngineService.GetAvailableUpgradesAsync");
        }

        return upgrades;
    }

    public async Task<List<WingetRow>> SearchAsync(string query, CancellationToken ct = default)
    {
        var results = new List<WingetRow>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return results;
        }

        string? winget = await GetWingetPathAsync().ConfigureAwait(false);
        if (winget == null)
        {
            return results;
        }

        try
        {
            ProcessExecutionResult result = await _processExecution.ExecuteAsync(winget,
                ["search", query, "--source", "winget", "--accept-source-agreements"],
                timeout: TimeSpan.FromMinutes(1), ct: ct).ConfigureAwait(false);

            bool headerPassed = false;
            foreach (string rawLine in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                ct.ThrowIfCancellationRequested();
                string line = rawLine.Trim();
                if (line.StartsWith("---"))
                {
                    headerPassed = true;
                    continue;
                }

                if (!headerPassed || line.Length == 0)
                {
                    continue;
                }

                string[] parts = MultiSpaceSplitRegex.Split(line);
                if (parts.Length >= 2)
                {
                    results.Add(new WingetRow(parts[0].Trim(), parts[1].Trim(),
                        parts.Length > 2 ? parts[2].Trim() : string.Empty, string.Empty));
                    if (results.Count >= 50)
                    {
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, $"WingetEngineService.SearchAsync({query})");
        }

        return results;
    }

    public async Task<InstallResult> UpgradeAsync(string packageId, IProgress<string>? log = null, CancellationToken ct = default)
    {
        string winget = await GetWingetPathAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("WinGet n'est pas disponible.");
        log?.Report($"Mise à jour : {packageId}");

        ProcessExecutionResult result = await _processExecution.ExecuteAsync(winget,
            ["upgrade", "--id", packageId, "--exact", "--source", "winget", "--silent",
             "--disable-interactivity", "--accept-package-agreements", "--accept-source-agreements"],
            timeout: TimeSpan.FromMinutes(15), ct: ct).ConfigureAwait(false);

        return TranslateExitCode(result.ExitCode);
    }

    public async Task<bool> InstallSimpleAsync(string packageId, CancellationToken ct = default)
    {
        InstallResult result = await InstallAsync(packageId, null, ct).ConfigureAwait(false);
        return result.Status is InstallStatus.Succeeded
            or InstallStatus.RestartRequired
            or InstallStatus.AlreadyUpToDate
            or InstallStatus.AlreadyInstalled;
    }

    public async Task<bool> UpgradeAllAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        List<WingetRow> upgrades = await GetAvailableUpgradesAsync(ct).ConfigureAwait(false);
        if (upgrades.Count == 0)
        {
            return true;
        }

        int failed = 0;
        foreach (WingetRow upgrade in upgrades)
        {
            ct.ThrowIfCancellationRequested();
            log?.Report($"Mise à jour : {upgrade.Name} ({upgrade.Id})...");
            InstallResult result = await UpgradeAsync(upgrade.Id, log, ct).ConfigureAwait(false);
            if (result.Status is not (InstallStatus.Succeeded
                or InstallStatus.RestartRequired
                or InstallStatus.AlreadyUpToDate
                or InstallStatus.AlreadyInstalled))
            {
                failed++;
            }
        }

        return failed == 0;
    }

    private static List<string> ExtractX64Dependencies(string zipPath, string dir)
    {
        var paths = new List<string>();
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string p = entry.FullName.Replace('\\', '/');
            if (p.StartsWith("x64/", StringComparison.OrdinalIgnoreCase)
                && p.EndsWith(".appx", StringComparison.OrdinalIgnoreCase))
            {
                string destination = Path.Combine(dir, Path.GetFileName(p));
                entry.ExtractToFile(destination, overwrite: true);
                paths.Add(destination);
            }
        }

        return paths.Count > 0
            ? paths
            : throw new InvalidOperationException("Dépendances x64 introuvables dans DesktopAppInstaller_Dependencies.zip.");
    }

    private async Task<bool> RepairAppInstallerAsync(IProgress<string>? log, CancellationToken ct)
    {
        try
        {
            const string script =
                "try { " +
                "$p = Get-AppxPackage Microsoft.DesktopAppInstaller -ErrorAction SilentlyContinue; " +
                "if ($null -eq $p) { $p = Get-AppxPackage -AllUsers Microsoft.DesktopAppInstaller -ErrorAction Stop }; " +
                "if ($null -eq $p) { exit 2 }; " +
                "$m = [System.IO.Path]::Combine($p.InstallLocation, 'AppxManifest.xml'); " +
                "if (-not (Test-Path -LiteralPath $m)) { exit 3 }; " +
                "Add-AppxPackage -Register $m -DisableDevelopmentMode; " +
                "if ($?) { exit 0 } else { exit 1 } " +
                "} catch { exit 4 }";

            ProcessExecutionResult result = await _processExecution.ExecuteAsync(
                "powershell.exe",
                ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script],
                timeout: TimeSpan.FromMinutes(2),
                ct: ct).ConfigureAwait(false);

            if (result is { Success: true, ExitCode: 0 })
            {
                return true;
            }

            log?.Report(result.ExitCode switch
            {
                2 => "App Installer absent du système - la réparation n'est pas possible.",
                3 => "Manifeste de l'App Installer introuvable.",
                4 => "Réparation bloquée : accès refusé (essaie en administrateur).",
                _ => "Réparation de l'App Installer échouée."
            });

            return false;
        }
        catch (Exception ex)
        {
            log?.Report($"Réparation impossible ({ex.Message}).");
            return false;
        }
    }

    private static async Task DownloadAsync(string url, string destination, CancellationToken ct)
    {
        using HttpResponseMessage response = await Http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using FileStream fs = File.Create(destination);
        await response.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
    }

    private static async Task<bool> VerifyBundleSha256Async(string bundlePath, IProgress<string>? log, CancellationToken ct)
    {
        try
        {
            await using FileStream fs = File.OpenRead(bundlePath);
            byte[] actual = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
            byte[] expected = Convert.FromHexString(BundleSha256);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "WingetEngineService.VerifyBundleSha256Async");
            log?.Report($"Impossible de vérifier l'empreinte du bundle : {ex.Message}");
            return false;
        }
    }

    private async Task<bool> AddAppxAsync(string packagePath, IProgress<string>? log, CancellationToken ct, params string[] extraArgs)
    {
        try
        {
            string options = extraArgs.Length > 0 ? " " + string.Join(" ", extraArgs) : string.Empty;
            ProcessExecutionResult result = await _processExecution.ExecuteAsync(
                "powershell.exe",
                ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $"Add-AppxPackage -Path \"{packagePath}\"{options}"],
                timeout: TimeSpan.FromMinutes(5),
                ct: ct).ConfigureAwait(false);

            if (result is { Success: true, ExitCode: 0 })
            {
                return true;
            }

            string? err = ErrorDetails(result);
            if (err != null)
            {
                log?.Report($"PowerShell : {err}");
            }

            return false;
        }
        catch (Exception ex)
        {
            log?.Report($"Installation impossible ({ex.Message}).");
            return false;
        }
    }

    private static string? ErrorDetails(ProcessExecutionResult result)
    {
        foreach (string? source in new[] { result.StandardError, result.StandardOutput, result.ErrorMessage })
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            string joined = string.Join(
                " | ",
                source
                    .Replace("\r\n", "\n")
                    .Split('\n')
                    .Select(static l => l.Trim())
                    .Where(static l => l.Length > 0)
                    .Take(3));

            if (joined.Length == 0)
            {
                continue;
            }

            return joined.Length > 400 ? joined[..400] + "…" : joined;
        }

        return null;
    }
}
