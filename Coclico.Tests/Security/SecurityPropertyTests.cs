using System.Text.RegularExpressions;
using Coclico.Security.Infrastructure;
using FsCheck.Xunit;

namespace Coclico.Tests;

public sealed class SecurityPropertyTests : IDisposable
{
    private readonly string _dir = Path.Join(Path.GetTempPath(), "coclico-proptests-" + Guid.NewGuid().ToString("N"));

    public SecurityPropertyTests()
    {
        _ = Directory.CreateDirectory(_dir);
    }

    [Property]
    public bool Sha256OfFileAlwaysMatches64LowercaseHexDigits(string? content)
    {
        string path = Path.Join(_dir, "sha-input.bin");
        File.WriteAllText(path, content ?? string.Empty);
        return Regex.IsMatch(FileIntegrityScanner.Sha256File(path), "^[0-9a-f]{64}$");
    }

    [Property]
    public bool PathsAcceptedByValidatorNeverContainColonOrLeadingSeparator(string? candidate)
    {
        if (candidate is null || !PathValidator.IsSafeRelativePath(candidate))
        {
            return true;
        }

        return !candidate.Contains(':') && !candidate.StartsWith('/') && !candidate.StartsWith('\\');
    }

    [Property]
    public bool EntriesAcceptedByArchiveGuardNeverContainColonOrLeadingSeparator(string? candidate)
    {
        if (candidate is null || !ArchiveGuard.IsSafeArchiveEntry(candidate))
        {
            return true;
        }

        return !candidate.Contains(':') && !candidate.StartsWith('/') && !candidate.StartsWith('\\');
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
    }
}
