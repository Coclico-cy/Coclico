namespace Coclico.Security.Domain;

public sealed class IntegrityManifest
{
    public int FormatVersion { get; set; } = 1;

    public string Product { get; set; } = "Coclico";

    public string ReleaseVersion { get; set; } = string.Empty;

    public int SecurityGeneration { get; set; }

    public string KeyId { get; set; } = string.Empty;

    public DateTime SignedUtc { get; set; } = DateTime.UtcNow;

    public List<ManifestFileEntry> Files { get; set; } = [];
}

public sealed class ManifestFileEntry
{
    public string Path { get; set; } = string.Empty;

    public long Bytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;
}

public sealed class SignedIntegrityManifest
{
    public IntegrityManifest Manifest { get; set; } = new();

    public string KeyId { get; set; } = string.Empty;

    public string Algorithm { get; set; } = "RSA-PSS-SHA256";

    public string Signature { get; set; } = string.Empty;
}
