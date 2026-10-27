using System.Security.Cryptography;
using Coclico.Security.Domain;

namespace Coclico.Security.Infrastructure;

public sealed class FileScanOutcome
{
    public List<string> Modified { get; } = [];

    public List<string> Missing { get; } = [];

    public List<string> Added { get; } = [];

    public int Checked { get; set; }

    public int AccessErrors { get; set; }
}

public static class FileIntegrityScanner
{
    public static FileScanOutcome Scan(string rootDir, IntegrityManifest manifest, IReadOnlySet<string> variableRelativePaths)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        FileScanOutcome outcome = new();

        var manifestPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ManifestFileEntry entry in manifest.Files)
        {
            string relative = PathValidator.NormalizeSeparators(entry.Path);
            manifestPaths.Add(relative);

            try
            {
                string fullPath = Path.Join(rootDir, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                {
                    outcome.Missing.Add(relative);
                    continue;
                }

                outcome.Checked++;
                if (!MatchesFile(fullPath, entry))
                {
                    outcome.Modified.Add(relative);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                outcome.AccessErrors++;
            }
        }

        try
        {
            foreach (string relative in Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories)
                .Select(file => PathValidator.NormalizeSeparators(Path.GetRelativePath(rootDir, file))))
            {
                if (IsVariable(relative, variableRelativePaths))
                {
                    continue;
                }

                if (!manifestPaths.Contains(relative))
                {
                    outcome.Added.Add(relative);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            outcome.AccessErrors++;
        }

        return outcome;
    }

    public static string Sha256File(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool MatchesFile(string fullPath, ManifestFileEntry entry)
    {
        var info = new FileInfo(fullPath);
        if (info.Length != entry.Bytes)
        {
            return false;
        }

        string hash = Sha256File(fullPath);
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(hash),
            Convert.FromHexString(entry.Sha256));
    }

    private static bool IsVariable(string relativePath, IReadOnlySet<string> variableRelativePaths)
    {
        string candidate = PathValidator.NormalizeForCompare(relativePath);
        return variableRelativePaths
            .Select(PathValidator.NormalizeForCompare)
            .Any(normalized => candidate.Equals(normalized, StringComparison.Ordinal) ||
                candidate.StartsWith(normalized + "/", StringComparison.Ordinal));
    }
}
