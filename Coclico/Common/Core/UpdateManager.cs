using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Coclico.Services;

public class UpdateManager(ILogger<UpdateManager> logger)
{
    private const string GitHubApiUrl = "https://api.github.com/repos";
    private const string GitHubOwner = "Coclico-cy";
    private const string GitHubRepo = "Coclico";
    private const long MaxInstallerSizeBytes = 2L * 1024 * 1024 * 1024;

    private static readonly HttpClient _httpClient = CreateHttpClient();

    private readonly ConcurrentDictionary<string, string> _verifiedAssets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<UpdateManager> _logger = logger;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add(
            "User-Agent",
            "Coclico-UpdateManager/1.0.0 (+https://github.com/" + GitHubOwner + "/" + GitHubRepo + ")"
        );
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    private static async Task<HttpResponseMessage> GetAsyncWithTimeoutAsync(
        string url, TimeSpan timeout, HttpCompletionOption completion, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return await _httpClient.GetAsync(url, completion, cts.Token);
    }

    public async Task<GitHubRelease?> CheckForUpdatesAsync(string currentVersion, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation($"Checking for updates... (current: {currentVersion})");

            string url = $"{GitHubApiUrl}/{GitHubOwner}/{GitHubRepo}/releases/latest";
            using HttpResponseMessage response = await GetAsyncWithTimeoutAsync(
                url, TimeSpan.FromSeconds(30), HttpCompletionOption.ResponseContentRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"GitHub API returned {response.StatusCode}");
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(ct) ?? string.Empty;
            GitHubRelease? release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = false
            });

            if (release == null)
            {
                _logger.LogWarning("Failed to parse GitHub release JSON");
                return null;
            }

            if (IsNewerVersion(release.TagName, currentVersion))
            {
                _logger.LogInformation($"Update available: {release.TagName} (current: {currentVersion})");
                return release;
            }

            _logger.LogInformation("Already up-to-date");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError($"HTTP error checking updates: {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError($"JSON parsing error: {ex.Message}");
            return null;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Update check cancelled");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected error checking updates: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> DownloadReleaseAsync(
        GitHubRelease release, string assetName, string savePath,
        IProgress<long>? progress = null, CancellationToken ct = default)
    {
        try
        {
            GitHubAsset? asset = release.Assets?.Find(a => string.Equals(a.Name, assetName, StringComparison.Ordinal));
            if (asset == null)
            {
                _logger.LogWarning($"Asset '{assetName}' not found in release {release.TagName}");
                return false;
            }

            if (!IsTrustedDownloadUrl(asset.BrowserDownloadUrl, out Uri? downloadUri))
            {
                _logger.LogError("Refusing update asset from an untrusted download URL.");
                return false;
            }

            if (asset.Size is <= 0 or > MaxInstallerSizeBytes)
            {
                _logger.LogError("Refusing update asset without a valid size.");
                return false;
            }

            string? expectedHash = await ResolveExpectedHashAsync(release, asset, assetName, ct);
            if (string.IsNullOrWhiteSpace(expectedHash) || !IsSha256Hex(expectedHash))
            {
                _logger.LogError("Refusing update asset without a valid SHA-256 digest.");
                return false;
            }

            using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            downloadCts.CancelAfter(TimeSpan.FromHours(2));
            using HttpResponseMessage assetResponse = await _httpClient
                .GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, downloadCts.Token)
                .ConfigureAwait(false);
            if (!assetResponse.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to download asset: {assetResponse.StatusCode}");
                return false;
            }

            if (assetResponse.Content.Headers.ContentLength is > MaxInstallerSizeBytes)
            {
                _logger.LogError("Refusing update asset: content length exceeds the size limit.");
                return false;
            }

            _ = Directory.CreateDirectory(Path.GetDirectoryName(savePath) ?? ".");
            string fullPath = Path.GetFullPath(savePath);
            string tempPath = fullPath + ".download-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (Stream contentStream = await assetResponse.Content.ReadAsStreamAsync(ct))
                await using (var fileStream = new FileStream(
                    tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
                {
                    byte[] buffer = new byte[128 * 1024];
                    long totalBytesRead = 0;
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        totalBytesRead += bytesRead;
                        progress?.Report(totalBytesRead);
                    }
                }

                await using FileStream hashStream = File.OpenRead(tempPath);
                string actualHash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, ct));
                if (!CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(actualHash), Convert.FromHexString(expectedHash)))
                {
                    _logger.LogError("Downloaded update failed SHA-256 verification.");
                    return false;
                }

                File.Move(tempPath, fullPath, overwrite: true);
                _verifiedAssets[fullPath] = expectedHash;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception exCleanup)
                {
                    LoggingService.LogException(exCleanup, "UpdateManager.TempCleanup");
                }
            }

            _logger.LogInformation($"Download complete: {savePath}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error downloading release: {ex.Message}");
            return false;
        }
    }

    public bool LaunchInstaller(string filePath, string? expectedVersion = null)
    {
        try
        {
            string fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath) || !_verifiedAssets.TryGetValue(fullPath, out string? expectedHash))
            {
                _logger.LogError("Refusing to launch an installer that was not verified by this update manager.");
                return false;
            }

            using FileStream stream = File.OpenRead(fullPath);
            string actualHash = Convert.ToHexString(SHA256.HashData(stream));
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash), Convert.FromHexString(expectedHash)))
            {
                _logger.LogError("Refusing to launch an installer whose contents changed after verification.");
                return false;
            }

            string extension = Path.GetExtension(fullPath);
            if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".msi", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError($"Refusing to launch an installer with unsupported extension '{extension}'.");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(expectedVersion) &&
                !InstallerVersionMatches(ReadInstallerVersion(fullPath), expectedVersion))
            {
                _logger.LogError($"Refusing to launch an installer whose file version does not match {expectedVersion}.");
                return false;
            }

            _logger.LogInformation($"Launching verified installer: {fullPath}");
            var psi = new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true,
                Verb = "runas"
            };
            _ = Process.Start(psi);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _logger.LogInformation("Installer launch cancelled by the user at the UAC prompt.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to launch installer: {ex.Message}");
            return false;
        }
    }

    internal static bool IsNewerVersion(string latestTag, string currentVersion)
    {
        try
        {
            string latest = latestTag.TrimStart('v', 'V');
            string current = currentVersion.TrimStart('v', 'V');
            (string latestBase, string latestSuffix) = SplitVersionAndSuffix(latest);
            (string currentBase, string currentSuffix) = SplitVersionAndSuffix(current);
            List<int> latestParts = ParseVersionParts(latestBase);
            List<int> currentParts = ParseVersionParts(currentBase);
            for (int i = 0; i < 4; i++)
            {
                int latestPart = i < latestParts.Count ? latestParts[i] : 0;
                int currentPart = i < currentParts.Count ? currentParts[i] : 0;

                if (latestPart > currentPart)
                {
                    return true;
                }

                if (latestPart < currentPart)
                {
                    return false;
                }
            }

            bool latestIsPrerelease = !string.IsNullOrEmpty(latestSuffix);
            bool currentIsPrerelease = !string.IsNullOrEmpty(currentSuffix);
            return !latestIsPrerelease && currentIsPrerelease;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "UpdateManager.IsNewerVersion");
            return string.Compare(latestTag, currentVersion) > 0;
        }
    }

    internal static bool IsSkipped(string? tagName, string? skippedVersion)
    {
        if (string.IsNullOrWhiteSpace(tagName) || string.IsNullOrWhiteSpace(skippedVersion))
        {
            return false;
        }

        static string Normalize(string value) => value.Trim().TrimStart('v', 'V');

        return string.Equals(Normalize(tagName), Normalize(skippedVersion), StringComparison.OrdinalIgnoreCase);
    }

    public static GitHubAsset? PickSetupAsset(GitHubRelease release)
    {
        return release.Assets?.FirstOrDefault(a => SetupAssetPattern.IsMatch(a.Name));
    }

    public static string? ReadInstallerVersion(string filePath)
    {
        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(filePath);
            string? version = string.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion : info.ProductVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, $"UpdateManager.ReadInstallerVersion({filePath})");
            return null;
        }
    }

    internal static bool InstallerVersionMatches(string? fileVersion, string expectedVersion)
    {
        if (string.IsNullOrWhiteSpace(fileVersion) || string.IsNullOrWhiteSpace(expectedVersion))
        {
            return false;
        }

        (string fileCore, string fileSuffix) = SplitNumericAndSuffix(fileVersion);
        (string expectedCore, string expectedSuffix) = SplitNumericAndSuffix(expectedVersion);

        return SameNumericVersion(fileCore, expectedCore) &&
            string.Equals(fileSuffix, expectedSuffix, StringComparison.OrdinalIgnoreCase);

        static (string core, string suffix) SplitNumericAndSuffix(string value)
        {
            string version = value.Trim().TrimStart('v', 'V');
            int buildIndex = version.IndexOf('+');
            if (buildIndex >= 0)
            {
                version = version[..buildIndex];
            }

            int cut = version.IndexOfAny(['-', ' ']);
            return cut > 0
                ? (version[..cut], version[(cut + 1)..])
                : (version, string.Empty);
        }

        static bool SameNumericVersion(string left, string right)
        {
            List<int> a = ParseVersionParts(left);
            List<int> b = ParseVersionParts(right);
            int length = Math.Max(a.Count, b.Count);
            for (int i = 0; i < length; i++)
            {
                if ((i < a.Count ? a[i] : 0) != (i < b.Count ? b[i] : 0))
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal static string? ParseHashForAsset(string shaContent, string assetName)
    {
        var hashByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int unnamedHashCount = 0;
        string? lastUnnamedHash = null;

        foreach (string rawLine in shaContent.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            Match m = Regex.Match(line, @"^([a-fA-F0-9]{64})\s+\*?(\S.*)$");
            if (m.Success)
            {
                string filename = m.Groups[2].Value.Trim().TrimStart('*');
                if (filename.StartsWith("./", StringComparison.Ordinal))
                {
                    filename = filename[2..];
                }

                hashByFile[filename] = m.Groups[1].Value;
                continue;
            }

            Match bare = Regex.Match(line, @"^([a-fA-F0-9]{64})$");
            if (bare.Success)
            {
                unnamedHashCount++;
                lastUnnamedHash = bare.Groups[1].Value;
            }
        }

        if (hashByFile.TryGetValue(assetName, out string? namedHash))
        {
            return namedHash;
        }

        return unnamedHashCount == 1 ? lastUnnamedHash : null;
    }

    private static bool IsTrustedDownloadUrl(string? url, out Uri? uri)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            (uri.Host != "github.com" && uri.Host != "objects.githubusercontent.com"))
        {
            return false;
        }

        return true;
    }

    private async Task<string?> ResolveExpectedHashAsync(
        GitHubRelease release, GitHubAsset asset, string assetName, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(asset.Digest))
        {
            return asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? asset.Digest[7..]
                : asset.Digest;
        }

        GitHubAsset? shaAsset = release.Assets?.Find(a =>
            a.Name.Equals(assetName + ".sha256", StringComparison.OrdinalIgnoreCase) ||
            a.Name.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase) ||
            a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase) ||
            a.Name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase));

        if (shaAsset == null || !Uri.TryCreate(shaAsset.BrowserDownloadUrl, UriKind.Absolute, out Uri? shaUri))
        {
            return null;
        }

        try
        {
            using var shaCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            shaCts.CancelAfter(TimeSpan.FromSeconds(30));
            string shaContent = await _httpClient.GetStringAsync(shaUri, shaCts.Token).ConfigureAwait(false);

            return ParseHashForAsset(shaContent, assetName);
        }
        catch (Exception exShaAsset)
        {
            LoggingService.LogException(exShaAsset, "UpdateManager.ShaAssetDownload");
            return null;
        }
    }

    private static (string versionPart, string suffixPart) SplitVersionAndSuffix(string version)
    {
        int dashIndex = version.IndexOf('-');
        if (dashIndex > 0)
        {
            return (version[..dashIndex], version[(dashIndex + 1)..]);
        }

        int spaceIndex = version.IndexOf(' ');
        if (spaceIndex > 0)
        {
            return (version[..spaceIndex], version[(spaceIndex + 1)..]);
        }

        return (version, string.Empty);
    }

    private static List<int> ParseVersionParts(string versionString)
    {
        var parts = new List<int>();
        foreach (string part in versionString.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out int num))
            {
                parts.Add(num);
            }
        }

        return parts;
    }

    private static bool IsSha256Hex(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        try
        {
            _ = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static readonly Regex SetupAssetPattern =
        new(@"^Coclico-\d+(\.\d+){0,3}\.exe$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
}

public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = [];

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;
}

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = string.Empty;

    [JsonPropertyName("digest")]
    public string Digest { get; set; } = string.Empty;
}
