using System.Text.Json;
using Coclico.Security.Domain;

namespace Coclico.Security.Infrastructure;

public static class ManifestLoader
{
    public static SignedIntegrityManifest? TryLoad(string manifestPath, out SecurityIncident incident, out string reason)
    {
        incident = SecurityIncident.None;
        reason = string.Empty;

        if (!File.Exists(manifestPath))
        {
            incident = SecurityIncident.ManifestMissing;
            reason = $"manifest not found at '{manifestPath}'";
            return null;
        }

        FileInfo info = new(manifestPath);
        if (info.Length > SecurityLimits.MaxManifestBytes)
        {
            incident = SecurityIncident.ManifestTooLarge;
            reason = $"manifest is {info.Length} bytes, limit is {SecurityLimits.MaxManifestBytes}";
            return null;
        }

        try
        {
            byte[] data = File.ReadAllBytes(manifestPath);
            SignedIntegrityManifest? signed = JsonSerializer.Deserialize(data, SecurityJsonContext.Default.SignedIntegrityManifest);
            if (signed?.Manifest == null || signed.Manifest.Files == null)
            {
                incident = SecurityIncident.ManifestMalformed;
                reason = "manifest structure is incomplete";
                return null;
            }

            if (signed.Manifest.Files.Count > SecurityLimits.MaxManifestEntries)
            {
                incident = SecurityIncident.ManifestMalformed;
                reason = $"manifest has {signed.Manifest.Files.Count} entries, limit is {SecurityLimits.MaxManifestEntries}";
                return null;
            }

            return signed;
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException or NotSupportedException)
        {
            incident = SecurityIncident.ManifestMalformed;
            reason = $"manifest cannot be parsed: {ex.Message}";
            return null;
        }
    }

    public static string Save(SignedIntegrityManifest signed, string outputPath)
    {
        string json = JsonSerializer.Serialize(signed, SecurityJsonContext.Default.SignedIntegrityManifest);
        File.WriteAllText(outputPath, json);
        return json;
    }
}
