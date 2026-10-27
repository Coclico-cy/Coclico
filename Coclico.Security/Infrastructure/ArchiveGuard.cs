namespace Coclico.Security.Infrastructure;

public static class ArchiveGuard
{
    public const int MaxEntries = 20000;
    public const long MaxEntryBytes = 2L * 1024 * 1024 * 1024;
    public const long MaxTotalBytes = 3L * 1024 * 1024 * 1024;
    public const int MaxPathChars = 255;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static bool IsSafeArchiveEntry(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || entryName.Length > MaxPathChars)
        {
            return false;
        }

        string name = entryName;
        if (name.EndsWith('/') || name.EndsWith('\\'))
        {
            name = name[..^1];
        }

        if (string.IsNullOrWhiteSpace(name) ||
            name.Contains(':') ||
            name.StartsWith('/') || name.StartsWith('\\') ||
            Path.IsPathRooted(name))
        {
            return false;
        }

        string[] segments = name.Split('/', '\\');
        foreach (string segment in segments)
        {
            if (string.IsNullOrEmpty(segment) || segment is "." or "..")
            {
                return false;
            }

            int dotIndex = segment.IndexOf('.');
            string stem = dotIndex > 0 ? segment[..dotIndex] : segment;
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

    public static bool IsEntryCountAllowed(int entryCount)
    {
        return entryCount > 0 && entryCount <= MaxEntries;
    }

    public static bool IsEntrySizeAllowed(long entryBytes, long accumulatedBytes)
    {
        return entryBytes >= 0 &&
            entryBytes <= MaxEntryBytes &&
            accumulatedBytes >= 0 &&
            accumulatedBytes + entryBytes <= MaxTotalBytes;
    }
}
