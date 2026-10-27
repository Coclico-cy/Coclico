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

            string script =
                $"$ws = New-Object -ComObject WScript.Shell; " +
                $"$s = $ws.CreateShortcut('{Esc(shortcutPath)}'); " +
                $"$s.TargetPath = '{Esc(targetExePath)}'; " +
                $"$s.WorkingDirectory = '{Esc(Path.GetDirectoryName(targetExePath) ?? string.Empty)}'; " +
                $"$s.Description = '{Esc(description)}'; " +
                (string.IsNullOrEmpty(iconPath) ? "" : $"$s.IconLocation = '{Esc(iconPath)},0'; ") +
                "$s.Save()";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "`\"")}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(15000);
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.InvalidOperationException or System.Runtime.InteropServices.COMException)
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
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur suppression raccourci {shortcutPath}: {ex.Message}");
        }
    }

    private static string Esc(string value) => value.Replace("'", "''");
}
