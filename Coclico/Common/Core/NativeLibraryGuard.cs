using System.IO;
using System.Runtime.InteropServices;

namespace Coclico.Services;

public static class NativeLibraryGuard
{
    private const uint LoadLibrarySearchDefaultDirs = 0x00001000;

    public static bool IsHardened { get; private set; }

    public static void Initialize()
    {
        try
        {
            if (OperatingSystem.IsWindows() && !IsHardened)
            {
                IsHardened = SetDefaultDllDirectories(LoadLibrarySearchDefaultDirs);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            IsHardened = false;
        }
    }

    public static bool IsNativeLoadPathAllowed(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        string baseDir;
        try
        {
            baseDir = Path.GetFullPath(AppContext.BaseDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return false;
        }

        string normalizedBase = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string candidate in EnumerateBlockedDirectories())
        {
            string normalized = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            {
                LoggingService.LogWarning($"[NativeLibraryGuard] Chargement natif refusé depuis un répertoire non fiable : '{path}'");
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> EnumerateBlockedDirectories()
    {
        yield return Path.GetTempPath();

        yield return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        yield return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);
}
