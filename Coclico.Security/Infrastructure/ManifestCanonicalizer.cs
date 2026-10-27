using System.Globalization;
using System.Text.Json;
using Coclico.Security.Domain;

namespace Coclico.Security.Infrastructure;

public static class ManifestCanonicalizer
{
    public static byte[] CanonicalBytes(IntegrityManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { SkipValidation = false });

        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", manifest.FormatVersion);
        writer.WriteString("product", manifest.Product ?? string.Empty);
        writer.WriteString("releaseVersion", manifest.ReleaseVersion ?? string.Empty);
        writer.WriteNumber("securityGeneration", manifest.SecurityGeneration);
        writer.WriteString("keyId", manifest.KeyId ?? string.Empty);
        writer.WriteString("signedUtc", DateTime.SpecifyKind(manifest.SignedUtc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture));
        writer.WriteStartArray("files");

        foreach (ManifestFileEntry entry in manifest.Files
            .OrderBy(e => e.Path ?? string.Empty, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("path", entry.Path ?? string.Empty);
            writer.WriteNumber("bytes", entry.Bytes);
            writer.WriteString("sha256", (entry.Sha256 ?? string.Empty).ToLowerInvariant());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return stream.ToArray();
    }
}
