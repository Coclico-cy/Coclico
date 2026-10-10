using System;
using System.IO;

namespace Coclico.Installer.Services;

public static class ShortcutHelper
{
    public static void CreateShortcut(string targetExePath, string shortcutPath, string description, string? iconPath = null)
    {
        try
        {
            string? directory = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetExePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetExePath);
            shortcut.Description = description;
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                shortcut.IconLocation = $"{iconPath},0";
            }
            shortcut.Save();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur création raccourci {shortcutPath}: {ex.Message}");
        }
    }

    public static void DeleteShortcut(string shortcutPath)
    {
        try
        {
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur suppression raccourci {shortcutPath}: {ex.Message}");
        }
    }
}