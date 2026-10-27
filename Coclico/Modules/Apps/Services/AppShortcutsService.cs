using System.IO;
using Coclico.Services;

namespace Coclico.Modules.Apps.Services;

public sealed class AppShortcutsService
{
    public static string GetShortcutDir()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
        {
            desktop = Path.GetTempPath();
        }

        return Path.Join(desktop, "Coclico Apps");
    }

    public int CopyStartMenuShortcuts(IEnumerable<string> appNames, IProgress<string>? log = null)
    {
        string shortcutDir = GetShortcutDir();
        Directory.CreateDirectory(shortcutDir);

        string[] startMenus =
        [
            Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs"),
            Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs")
        ];

        string[] names = appNames.Where(static n => !string.IsNullOrWhiteSpace(n)).ToArray();
        int copied = 0;

        foreach (string startMenu in startMenus.Where(Directory.Exists))
        {
            foreach (string shortcut in Directory.EnumerateFiles(startMenu, "*.lnk", SearchOption.AllDirectories))
            {
                string baseName = Path.GetFileNameWithoutExtension(shortcut);
                if (!names.Any(n => Matches(baseName, n)))
                {
                    continue;
                }

                string target = Path.Join(shortcutDir, Path.GetFileName(shortcut));
                try
                {
                    File.Copy(shortcut, target, overwrite: true);
                    copied++;
                }
                catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
                {
                    LoggingService.LogException(ex, $"AppShortcutsService.CopyStartMenuShortcuts({target})");
                }
            }
        }

        log?.Report($"{copied} raccourci(s) copié(s) dans {shortcutDir}");
        return copied;
    }

    private static bool Matches(string baseName, string name)
    {
        if (baseName.Contains(name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string word in name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            if (word.Length >= 4 && baseName.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
