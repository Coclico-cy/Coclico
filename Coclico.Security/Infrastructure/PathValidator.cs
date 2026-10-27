using Coclico.Security.Domain;

namespace Coclico.Security.Infrastructure;

public static class PathValidator
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Length > SecurityLimits.MaxEntryPathChars ||
            path.Contains(':', StringComparison.Ordinal) ||
            path.StartsWith('\\') || path.StartsWith('/') ||
            Path.IsPathRooted(path))
        {
            return false;
        }

        string[] segments = path.Split('/', '\\');
        foreach (string segment in segments)
        {
            if (string.IsNullOrEmpty(segment) || segment is "." or "..")
            {
                return false;
            }

            string name = segment;
            int dotIndex = name.IndexOf('.');
            string stem = dotIndex > 0 ? name[..dotIndex] : name;
            if (ReservedNames.Contains(stem))
            {
                return false;
            }

            if (segment.EndsWith('.') || segment.EndsWith(' '))
            {
                return false;
            }

            foreach (char c in segment)
            {
                if (c is '<' or '>' or '"' or '|' or '?' or '*' || char.IsControl(c))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static string NormalizeSeparators(string path)
    {
        return path.Replace('\\', '/');
    }

    public static string NormalizeForCompare(string path)
    {
        return NormalizeSeparators(path).TrimEnd('/').ToUpperInvariant();
    }

    public static bool HasDuplicates(IEnumerable<string> paths, out string duplicate)
    {
        duplicate = string.Empty;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            string key = NormalizeForCompare(path);
            if (!seen.Add(key))
            {
                duplicate = path;
                return true;
            }
        }

        return false;
    }
}
