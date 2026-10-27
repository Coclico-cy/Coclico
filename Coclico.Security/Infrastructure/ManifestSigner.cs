using System.Security.Cryptography;
using Coclico.Security.Domain;

namespace Coclico.Security.Infrastructure;

public static class ManifestSigner
{
    public static string Sign(IntegrityManifest manifest, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(privateKey);

        byte[] payload = ManifestCanonicalizer.CanonicalBytes(manifest);
        byte[] signature = privateKey.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        return Convert.ToBase64String(signature);
    }

    public static SignedIntegrityManifest CreateSigned(IntegrityManifest manifest, RSA privateKey, string keyId)
    {
        return new SignedIntegrityManifest
        {
            Manifest = manifest,
            KeyId = keyId,
            Algorithm = SecurityLimits.SignatureAlgorithm,
            Signature = Sign(manifest, privateKey)
        };
    }

    public static bool VerifySignature(SignedIntegrityManifest signed, TrustPolicy policy, out SecurityIncident incident, out string reason)
    {
        ArgumentNullException.ThrowIfNull(signed);
        ArgumentNullException.ThrowIfNull(policy);
        incident = SecurityIncident.None;
        reason = string.Empty;

        if (!string.Equals(signed.Algorithm, SecurityLimits.SignatureAlgorithm, StringComparison.Ordinal))
        {
            incident = SecurityIncident.ManifestMalformed;
            reason = $"unsupported algorithm '{signed.Algorithm}'";
            return false;
        }

        string signedKeyId = string.IsNullOrWhiteSpace(signed.KeyId) ? signed.Manifest.KeyId : signed.KeyId;
        DateTime signedAt = DateTime.SpecifyKind(signed.Manifest.SignedUtc, DateTimeKind.Utc);
        if (signedAt > DateTime.UtcNow.AddMinutes(30))
        {
            incident = SecurityIncident.ManifestMalformed;
            reason = "signedUtc is in the future";
            return false;
        }

        TrustedKey? key = policy.FindActiveKey(signedKeyId, signedAt);
        if (key == null)
        {
            TrustedKey? known = policy.Keys.FirstOrDefault(k => string.Equals(k.KeyId, signedKeyId, StringComparison.Ordinal));
            incident = known is { RevokedUtc: not null } ? SecurityIncident.KeyRevoked : SecurityIncident.KeyUnknown;
            reason = $"keyId '{signedKeyId}' is not trusted";
            return false;
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signed.Signature);
        }
        catch (FormatException)
        {
            incident = SecurityIncident.SignatureInvalid;
            reason = "signature is not valid base64";
            return false;
        }

        using var rsa = RSA.Create();
        try
        {
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key.PublicKeyBase64), out _);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            incident = SecurityIncident.ManifestMalformed;
            reason = "trusted public key cannot be imported";
            return false;
        }

        byte[] payload = ManifestCanonicalizer.CanonicalBytes(signed.Manifest);
        bool valid = rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        if (!valid)
        {
            incident = SecurityIncident.SignatureInvalid;
            reason = "signature does not match the signed manifest";
            return false;
        }

        return true;
    }
}
