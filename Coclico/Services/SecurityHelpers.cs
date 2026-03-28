#nullable enable
using System;
using System.IO;

namespace Coclico.Services;

internal static class SecurityHelpers
{
    public static bool IsPathSafe(string? path, string allowedRoot)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (string.IsNullOrWhiteSpace(allowedRoot)) return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(allowedRoot);

            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
                fullRoot += Path.DirectorySeparatorChar;

            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, $"SecurityHelpers.IsPathSafe({path})");
            return false;
        }
    }

    public static bool IsFileNameSafe(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;

        var invalidChars = Path.GetInvalidFileNameChars();
        return fileName.IndexOfAny(invalidChars) < 0;
    }

    public static string? SanitizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "SecurityHelpers.SanitizePath");
            return null;
        }
    }
}
