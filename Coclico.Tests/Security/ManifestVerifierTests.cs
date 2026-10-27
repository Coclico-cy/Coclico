using Coclico.Security.Domain;
using Coclico.Security.Infrastructure;
using Xunit;

namespace Coclico.Tests;

public sealed class ManifestVerifierTests : IDisposable
{
    private readonly TestWorld _world = new();

    [Fact]
    public void ValidManifestPasses()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");
        _ = _world.WriteFile("Coclico.Core.dll", "library-content");
        _ = _world.SaveManifest(_world.BuildManifest());

        VerificationResult result = _world.Verify();

        Assert.Equal(IntegrityStatus.Valid, result.Status);
        Assert.Equal(2, result.CheckedFiles);
    }

    [Fact]
    public void ModifiedFileIsAltered()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");
        _ = _world.SaveManifest(_world.BuildManifest());
        _ = _world.WriteFile("Coclico.exe", "tampered-content");

        VerificationResult result = _world.Verify();

        Assert.Equal(IntegrityStatus.Altered, result.Status);
        Assert.Equal(SecurityIncident.FileModified, result.Incident);
        Assert.Contains("Coclico.exe", result.ModifiedFiles);
    }

    [Fact]
    public void MissingFileIsAltered()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");
        _ = _world.SaveManifest(_world.BuildManifest());
        File.Delete(Path.Join(_world.RootDir, "Coclico.exe"));

        VerificationResult result = _world.Verify();

        Assert.Equal(IntegrityStatus.Altered, result.Status);
        Assert.Equal(SecurityIncident.FileMissing, result.Incident);
    }

    [Fact]
    public void AddedFileIsAltered()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");
        _ = _world.SaveManifest(_world.BuildManifest());
        _ = _world.WriteFile("evil.dll", "injected");

        VerificationResult result = _world.Verify();

        Assert.Equal(IntegrityStatus.Altered, result.Status);
        Assert.Contains("evil.dll", result.AddedFiles);
    }

    [Fact]
    public void VariableFilesAreIgnored()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");
        _ = _world.SaveManifest(_world.BuildManifest());
        _ = _world.WriteFile("logs/app.log", "noise");

        var variable = new HashSet<string> { "logs" };
        VerificationResult result = _world.Verify(variable);

        Assert.Equal(IntegrityStatus.Valid, result.Status);
    }

    [Fact]
    public void MissingManifestIsUnknown()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");

        VerificationResult result = _world.Verify();

        Assert.Equal(IntegrityStatus.Unknown, result.Status);
        Assert.Equal(SecurityIncident.ManifestMissing, result.Incident);
    }

    [Fact]
    public void TamperedManifestFailsSignature()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");
        SignedIntegrityManifest signed = _world.BuildManifest();
        signed.Manifest.ReleaseVersion = "9.9.9";
        _ = _world.SaveManifest(signed);

        VerificationResult result = _world.Verify();

        Assert.Equal(IntegrityStatus.Unknown, result.Status);
        Assert.Equal(SecurityIncident.SignatureInvalid, result.Incident);
    }

    [Fact]
    public void UnsignedManifestIsRejected()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");
        _ = _world.SaveManifest(_world.BuildManifest(sign: false));

        VerificationResult result = _world.Verify();

        Assert.Equal(IntegrityStatus.Unknown, result.Status);
        Assert.Equal(SecurityIncident.SignatureInvalid, result.Incident);
    }

    [Fact]
    public void UnknownKeyIsRejected()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");
        SignedIntegrityManifest signed = _world.BuildManifest();
        signed.KeyId = "coclico-release-ffffffffffffffff";
        signed.Manifest.KeyId = signed.KeyId;
        _ = _world.SaveManifest(signed);

        VerificationResult result = _world.Verify();

        Assert.Equal(IntegrityStatus.Unknown, result.Status);
        Assert.Equal(SecurityIncident.KeyUnknown, result.Incident);
    }

    [Fact]
    public void RevokedKeyIsRejected()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");
        _world.SaveManifest(_world.BuildManifest());
        _world.Policy.Keys[0].RevokedUtc = DateTime.UtcNow.AddMinutes(-2);

        VerificationResult result = _world.Verify();

        Assert.Equal(IntegrityStatus.Unknown, result.Status);
        Assert.Equal(SecurityIncident.KeyRevoked, result.Incident);
    }

    [Fact]
    public void UnsupportedFormatVersionIsRejected()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");
        SignedIntegrityManifest signed = _world.BuildManifest();
        signed.Manifest.FormatVersion = 7;
        _ = _world.SaveManifest(ManifestSigner.CreateSigned(signed.Manifest, _world.PrivateKey, _world.KeyId));

        VerificationResult result = _world.Verify();

        Assert.Equal(IntegrityStatus.Unknown, result.Status);
        Assert.Equal(SecurityIncident.FormatVersionUnsupported, result.Incident);
    }

    [Fact]
    public void WrongProductIsRejected()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");
        SignedIntegrityManifest signed = _world.BuildManifest();
        signed.Manifest.Product = "NotCoclico";
        _ = _world.SaveManifest(ManifestSigner.CreateSigned(signed.Manifest, _world.PrivateKey, _world.KeyId));

        VerificationResult result = _world.Verify();

        Assert.Equal(IntegrityStatus.Unknown, result.Status);
        Assert.Equal(SecurityIncident.ProductMismatch, result.Incident);
    }

    [Fact]
    public void MalformedManifestIsRejected()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");
        File.WriteAllText(_world.ManifestPath, "{ this is not json");

        VerificationResult result = _world.Verify();

        Assert.Equal(IntegrityStatus.Unknown, result.Status);
        Assert.Equal(SecurityIncident.ManifestMalformed, result.Incident);
    }

    [Fact]
    public void DuplicateEntryIsRejected()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");
        SignedIntegrityManifest signed = _world.BuildManifest();
        signed.Manifest.Files.Add(signed.Manifest.Files[0]);
        _ = _world.SaveManifest(ManifestSigner.CreateSigned(signed.Manifest, _world.PrivateKey, _world.KeyId));

        VerificationResult result = _world.Verify();

        Assert.Equal(IntegrityStatus.Unknown, result.Status);
        Assert.Equal(SecurityIncident.DuplicateEntry, result.Incident);
    }

    [Fact]
    public void UnsafeManifestPathIsRejected()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");
        SignedIntegrityManifest signed = _world.BuildManifest();
        signed.Manifest.Files.Add(new ManifestFileEntry
        {
            Path = "../../Windows/System32/evil.dll",
            Bytes = 1,
            Sha256 = new string('a', 64)
        });
        _ = _world.SaveManifest(ManifestSigner.CreateSigned(signed.Manifest, _world.PrivateKey, _world.KeyId));

        VerificationResult result = _world.Verify();

        Assert.Equal(IntegrityStatus.Unknown, result.Status);
        Assert.Equal(SecurityIncident.PathRejected, result.Incident);
    }

    [Fact]
    public void OversizedManifestIsRejected()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");
        _ = _world.SaveManifest(_world.BuildManifest());
        File.WriteAllText(_world.ManifestPath, new string('x', SecurityLimits.MaxManifestBytes + 1));

        VerificationResult result = _world.Verify();

        Assert.Equal(IntegrityStatus.Unknown, result.Status);
        Assert.Equal(SecurityIncident.ManifestTooLarge, result.Incident);
    }

    public void Dispose()
    {
        _world.Dispose();
    }
}
