using System.IO;
using System.Net.Http;
using System.Text.Json;
using Coclico.Security.Domain;
using Coclico.Security.Infrastructure;

namespace Coclico.Services;

public sealed class ReleaseAuthenticationResult
{
    public bool IsTrustedKeyConfigured { get; init; }

    public bool IsAuthenticated { get; init; }

    public int SecurityGeneration { get; init; }

    public string Reason { get; init; } = string.Empty;
}

public static class ReleaseAuthentication
{
    private const string ManifestAssetName = "integrity.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 8
    };

    public static async Task<ReleaseAuthenticationResult> AuthenticateAsync(
        HttpClient httpClient,
        GitHubRelease release,
        TrustPolicy trustPolicy,
        CancellationToken ct)
    {
        GitHubAsset? manifestAsset = release.Assets?.Find(a =>
            a.Name.Equals(ManifestAssetName, StringComparison.OrdinalIgnoreCase));

        if (manifestAsset == null)
        {
            if (trustPolicy.Keys.Count > 0)
            {
                return new ReleaseAuthenticationResult
                {
                    IsTrustedKeyConfigured = true,
                    IsAuthenticated = false,
                    Reason = "release has no signed integrity manifest"
                };
            }

            return new ReleaseAuthenticationResult
            {
                IsTrustedKeyConfigured = false,
                IsAuthenticated = false,
                Reason = "release has no signed manifest and no trusted key is configured, falling back to SHA-256 sums"
            };
        }

        if (manifestAsset.Size is <= 0 or > SecurityLimits.MaxManifestBytes ||
            !Uri.TryCreate(manifestAsset.BrowserDownloadUrl, UriKind.Absolute, out Uri? downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            return new ReleaseAuthenticationResult
            {
                IsTrustedKeyConfigured = true,
                IsAuthenticated = false,
                Reason = "signed manifest asset is not usable"
            };
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            byte[] manifestBytes = await httpClient.GetByteArrayAsync(downloadUri, cts.Token).ConfigureAwait(false);
            if (manifestBytes.Length > SecurityLimits.MaxManifestBytes)
            {
                return new ReleaseAuthenticationResult
                {
                    IsTrustedKeyConfigured = true,
                    IsAuthenticated = false,
                    Reason = "signed manifest exceeds the size limit"
                };
            }

            SignedIntegrityManifest? signed = JsonSerializer.Deserialize<SignedIntegrityManifest>(manifestBytes, Options);
            if (signed?.Manifest == null)
            {
                return new ReleaseAuthenticationResult
                {
                    IsTrustedKeyConfigured = true,
                    IsAuthenticated = false,
                    Reason = "signed manifest cannot be parsed"
                };
            }

            if (trustPolicy.Keys.Count == 0)
            {
                return new ReleaseAuthenticationResult
                {
                    IsTrustedKeyConfigured = false,
                    IsAuthenticated = false,
                    SecurityGeneration = signed.Manifest.SecurityGeneration,
                    Reason = "no trusted key is configured, falling back to SHA-256 sums"
                };
            }

            if (!ManifestSigner.VerifySignature(signed, trustPolicy, out SecurityIncident incident, out string reason))
            {
                return new ReleaseAuthenticationResult
                {
                    IsTrustedKeyConfigured = true,
                    IsAuthenticated = false,
                    SecurityGeneration = signed.Manifest.SecurityGeneration,
                    Reason = $"manifest rejected: {incident} {reason}"
                };
            }

            return new ReleaseAuthenticationResult
            {
                IsTrustedKeyConfigured = true,
                IsAuthenticated = true,
                SecurityGeneration = signed.Manifest.SecurityGeneration,
                Reason = $"signed by {signed.Manifest.KeyId}"
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            return new ReleaseAuthenticationResult
            {
                IsTrustedKeyConfigured = true,
                IsAuthenticated = false,
                Reason = $"manifest could not be downloaded or read: {ex.Message}"
            };
        }
    }
}
