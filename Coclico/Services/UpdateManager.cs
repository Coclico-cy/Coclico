using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Coclico.Services
{
    public class UpdateManager
    {
        private static readonly HttpClient _httpClient = new();
        private const string GitHubApiUrl = "https://api.github.com/repos";
        // ⚠️ CONFIGURE THESE WITH YOUR GITHUB USERNAME AND REPO NAME
        private const string GitHubOwner = "Coclico-cy";
        private const string GitHubRepo = "Coclico";

        private readonly ILogger<UpdateManager> _logger;
        private readonly SettingsService _settingsService;

        public UpdateManager(ILogger<UpdateManager> logger, SettingsService settingsService)
        {
            _logger = logger;
            _settingsService = settingsService;
            _httpClient.DefaultRequestHeaders.Add(
                "User-Agent",
                "Coclico-UpdateManager/1.0.0 (+https://github.com/" + GitHubOwner + "/" + GitHubRepo + ")"
            );
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<GitHubRelease> CheckForUpdatesAsync(string currentVersion, CancellationToken ct = default)
        {
            try
            {
                _logger.LogInformation($"Checking for updates... (current: {currentVersion})");

                var url = $"{GitHubApiUrl}/{GitHubOwner}/{GitHubRepo}/releases/latest";
                var response = await _httpClient.GetAsync(url, ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"GitHub API returned {response.StatusCode}");
                    return null!;
                }

                var json = await response.Content.ReadAsStringAsync(ct) ?? string.Empty;
                var release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    WriteIndented = false
                });

                if (release == null)
                {
                    _logger.LogWarning("Failed to parse GitHub release JSON");
                    return null!;
                }

                if (IsNewerVersion(release.TagName, currentVersion))
                {
                    _logger.LogInformation($"Update available: {release.TagName} (current: {currentVersion})");
                    return release;
                }
                else
                {
                    _logger.LogInformation("Already up-to-date");
                    return null!;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"HTTP error checking updates: {ex.Message}");
                return null!;
            }
            catch (JsonException ex)
            {
                _logger.LogError($"JSON parsing error: {ex.Message}");
                return null!;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Update check cancelled");
                return null!;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error checking updates: {ex.Message}");
                return null!;
            }
        }

        private static bool IsNewerVersion(string latestTag, string currentVersion)
        {
            try
            {
                var latest = latestTag.TrimStart('v', 'V');
                var current = currentVersion.TrimStart('v', 'V');
                var (latestBase, latestSuffix) = SplitVersionAndSuffix(latest);
                var (currentBase, currentSuffix) = SplitVersionAndSuffix(current);
                var latestParts = ParseVersionParts(latestBase);
                var currentParts = ParseVersionParts(currentBase);
                for (int i = 0; i < 4; i++)
                {
                    int latestPart = i < latestParts.Count ? latestParts[i] : 0;
                    int currentPart = i < currentParts.Count ? currentParts[i] : 0;

                    if (latestPart > currentPart) return true;
                    if (latestPart < currentPart) return false;
                }

                bool latestIsPrerelease = !string.IsNullOrEmpty(latestSuffix);
                bool currentIsPrerelease = !string.IsNullOrEmpty(currentSuffix);
                return !latestIsPrerelease && currentIsPrerelease;
            }
            catch
            {
                return string.Compare(latestTag, currentVersion) > 0;
            }
        }

        private static (string versionPart, string suffixPart) SplitVersionAndSuffix(string version)
        {
            var dashIndex = version.IndexOf('-');
            if (dashIndex > 0)
            {
                return (version[..dashIndex], version[(dashIndex + 1)..]);
            }

            var spaceIndex = version.IndexOf(' ');
            if (spaceIndex > 0)
            {
                return (version[..spaceIndex], version[(spaceIndex + 1)..]);
            }

            return (version, string.Empty);
        }

        private static List<int> ParseVersionParts(string versionString)
        {
            var parts = new List<int>();
            foreach (var part in versionString.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part, out int num))
                {
                    parts.Add(num);
                }
            }
            return parts;
        }

        public async Task<bool> DownloadReleaseAsync(GitHubRelease release, string assetName, string savePath, CancellationToken ct = default)
        {
            try
            {
                var asset = release.Assets?.Find(a => a.Name.Contains(assetName, StringComparison.OrdinalIgnoreCase));
                if (asset == null)
                {
                    _logger.LogWarning($"Asset '{assetName}' not found in release {release.TagName}");
                    return false;
                }

                _logger.LogInformation($"Downloading {asset.Name} to {savePath}...");

                var assetResponse = await _httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!assetResponse.IsSuccessStatusCode)
                {
                    _logger.LogError($"Failed to download asset: {assetResponse.StatusCode}");
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(savePath) ?? ".");
                using (var contentStream = await assetResponse.Content.ReadAsStreamAsync(ct))
                using (var fileStream = File.Create(savePath))
                {
                    await contentStream.CopyToAsync(fileStream, ct);
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

        public void LaunchInstaller(string filePath)
        {
            try
            {
                _logger.LogInformation($"Launching installer: {filePath}");
                var psi = new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to launch installer: {ex.Message}");
            }
        }
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
        public List<GitHubAsset> Assets { get; set; } = new();

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
    }

    public interface ILogger<T>
    {
        void LogInformation(string message);
        void LogWarning(string message);
        void LogError(string message);
    }

    /// <summary>
    /// Null logger implementation for I logger Interface
    /// </summary>
    public class NullLogger<T> : ILogger<T>
    {
        public void LogInformation(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message) { }
    }
}
