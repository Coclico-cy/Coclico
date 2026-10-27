using Coclico.Security.Domain;

namespace Coclico.Security.Infrastructure;

public static class IntegrityVerifier
{
    public static VerificationResult VerifyDirectory(
        string rootDir,
        string manifestPath,
        TrustPolicy policy,
        IReadOnlySet<string>? variableRelativePaths = null)
    {
        ArgumentNullException.ThrowIfNull(policy);

        SignedIntegrityManifest? signed = ManifestLoader.TryLoad(manifestPath, out SecurityIncident incident, out string reason);
        if (signed == null)
        {
            return VerificationResult.Unknown(incident, reason);
        }

        if (signed.Manifest.FormatVersion != SecurityLimits.SupportedFormatVersion)
        {
            return VerificationResult.Unknown(
                SecurityIncident.FormatVersionUnsupported,
                $"formatVersion {signed.Manifest.FormatVersion} is not supported");
        }

        if (!string.Equals(signed.Manifest.Product, "Coclico", StringComparison.Ordinal))
        {
            return VerificationResult.Unknown(
                SecurityIncident.ProductMismatch,
                $"product '{signed.Manifest.Product}' is not Coclico");
        }

        if (!ManifestSigner.VerifySignature(signed, policy, out incident, out reason))
        {
            return VerificationResult.Unknown(incident, reason);
        }

        if (PathValidator.HasDuplicates(signed.Manifest.Files.Select(f => f.Path), out string duplicate))
        {
            return VerificationResult.Unknown(
                SecurityIncident.DuplicateEntry,
                $"duplicate manifest entry '{duplicate}'");
        }

        foreach (ManifestFileEntry entry in signed.Manifest.Files)
        {
            if (!PathValidator.IsSafeRelativePath(entry.Path))
            {
                return VerificationResult.Unknown(
                    SecurityIncident.PathRejected,
                    $"rejected manifest path '{entry.Path}'");
            }

            if (entry.Bytes <= 0 || !IsSha256Hex(entry.Sha256))
            {
                return VerificationResult.Unknown(
                    SecurityIncident.ManifestMalformed,
                    $"invalid entry data for '{entry.Path}'");
            }
        }

        HashSet<string> scanExclusions = variableRelativePaths is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(variableRelativePaths, StringComparer.OrdinalIgnoreCase);
        try
        {
            string manifestRelative = PathValidator.NormalizeSeparators(Path.GetRelativePath(rootDir, Path.GetFullPath(manifestPath)));
            scanExclusions.Add(manifestRelative);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return VerificationResult.Unknown(SecurityIncident.AccessError, "manifest path cannot be resolved against the root directory");
        }

        FileScanOutcome scan = FileIntegrityScanner.Scan(rootDir, signed.Manifest, scanExclusions);
        if (scan.Modified.Count > 0 || scan.Missing.Count > 0 || scan.Added.Count > 0)
        {
            SecurityIncident scanIncident = scan.Missing.Count > 0 ? SecurityIncident.FileMissing :
                scan.Modified.Count > 0 ? SecurityIncident.FileModified : SecurityIncident.FileAdded;
            return VerificationResult.Altered(
                scanIncident,
                $"files modified={scan.Modified.Count} missing={scan.Missing.Count} added={scan.Added.Count}",
                scan.Checked,
                scan.Modified,
                scan.Missing,
                scan.Added);
        }

        if (scan.AccessErrors > 0)
        {
            return VerificationResult.Unknown(
                SecurityIncident.AccessError,
                $"{scan.AccessErrors} files could not be read",
                scan.Checked);
        }

        return VerificationResult.Valid(scan.Checked);
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
}
