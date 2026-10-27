using System.Security.Cryptography;
using Coclico.Security.Domain;
using Coclico.Security.Infrastructure;

namespace Coclico.Tests;

public sealed class TestWorld : IDisposable
{
    public string RootDir { get; }

    public string ManifestPath { get; }

    public RSA PrivateKey { get; }

    public string KeyId { get; }

    public TrustPolicy Policy { get; }

    public TestWorld()
    {
        RootDir = Path.Join(Path.GetTempPath(), "coclico-tests-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(RootDir);
        ManifestPath = Path.Join(RootDir, "integrity.json");
        PrivateKey = RSA.Create(2048);
        KeyId = KeyfileProtector.ComputeKeyId(KeyfileProtector.ExportPublicKeyBase64(PrivateKey));
        Policy = new TrustPolicy
        {
            Keys =
            [
                new TrustedKey
                {
                    KeyId = KeyId,
                    PublicKeyBase64 = KeyfileProtector.ExportPublicKeyBase64(PrivateKey),
                    ActiveFromUtc = DateTime.UtcNow.AddHours(-1)
                }
            ]
        };
    }

    public string WriteFile(string relativePath, string content)
    {
        string full = Path.Join(RootDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        string? dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
        {
            _ = Directory.CreateDirectory(dir);
        }

        File.WriteAllText(full, content);
        return full;
    }

    public SignedIntegrityManifest BuildManifest(string version = "2.2.0", int generation = 1, bool sign = true)
    {
        List<ManifestFileEntry> entries = [];
        foreach (string file in Directory.EnumerateFiles(RootDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(RootDir, file).Replace('\\', '/');
            if (relative.Equals("integrity.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entries.Add(new ManifestFileEntry
            {
                Path = relative,
                Bytes = new FileInfo(file).Length,
                Sha256 = FileIntegrityScanner.Sha256File(file)
            });
        }

        IntegrityManifest manifest = new()
        {
            FormatVersion = SecurityLimits.SupportedFormatVersion,
            Product = "Coclico",
            ReleaseVersion = version,
            SecurityGeneration = generation,
            KeyId = KeyId,
            SignedUtc = DateTime.UtcNow.AddMinutes(-1),
            Files = entries.OrderBy(e => e.Path, StringComparer.Ordinal).ToList()
        };

        if (!sign)
        {
            return new SignedIntegrityManifest { Manifest = manifest, KeyId = KeyId, Signature = "AAAA" };
        }

        return ManifestSigner.CreateSigned(manifest, PrivateKey, KeyId);
    }

    public string SaveManifest(SignedIntegrityManifest signed)
    {
        _ = ManifestLoader.Save(signed, ManifestPath);
        return ManifestPath;
    }

    public VerificationResult Verify(IReadOnlySet<string>? variablePaths = null)
    {
        return IntegrityVerifier.VerifyDirectory(RootDir, ManifestPath, Policy, variablePaths);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(RootDir, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
    }
}
