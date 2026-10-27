using System.IO;
using Microsoft.Win32;

namespace Coclico.Services;

public sealed record StartupEntry(
    string Name,
    string Command,
    string Source,
    bool IsEnabled,
    bool CanToggle);

public static class StartupManagerService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedFolderKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    public static List<StartupEntry> ListStartupEntries()
    {
        var entries = new List<StartupEntry>();

        try
        {
            using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (runKey != null)
            {
                foreach (string name in runKey.GetValueNames())
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    string? command = runKey.GetValue(name)?.ToString();
                    entries.Add(new StartupEntry(
                        Name: name,
                        Command: command ?? string.Empty,
                        Source: "HKCU",
                        IsEnabled: IsRegistryEntryEnabled(name),
                        CanToggle: true));
                }
            }
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "StartupManagerService.ListHkcu");
        }

        try
        {
            using RegistryKey? runKey = Registry.LocalMachine.OpenSubKey(RunKeyPath);
            if (runKey != null)
            {
                foreach (string name in runKey.GetValueNames())
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    string? command = runKey.GetValue(name)?.ToString();
                    entries.Add(new StartupEntry(
                        Name: name,
                        Command: command ?? string.Empty,
                        Source: "HKLM",
                        IsEnabled: true,
                        CanToggle: false));
                }
            }
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "StartupManagerService.ListHklm");
        }

        try
        {
            string startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startupDir))
            {
                foreach (string file in Directory.EnumerateFiles(startupDir))
                {
                    string fileName = Path.GetFileName(file);
                    entries.Add(new StartupEntry(
                        Name: fileName,
                        Command: file,
                        Source: "Dossier",
                        IsEnabled: IsFolderEntryEnabled(fileName),
                        CanToggle: true));
                }
            }
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "StartupManagerService.ListStartupFolder");
        }

        return entries;
    }

    public static bool SetEntryEnabled(StartupEntry entry, bool enabled)
    {
        if (entry is null || !entry.CanToggle)
        {
            return false;
        }

        try
        {
            string keyPath = entry.Source == "Dossier" ? ApprovedFolderKeyPath : ApprovedKeyPath;

            using RegistryKey? approved = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
            if (approved == null)
            {
                return false;
            }

            approved.SetValue(entry.Name, BuildApprovalValue(enabled), RegistryValueKind.Binary);

            LoggingService.LogInfo(
                $"[StartupManagerService] Entrée de démarrage « {entry.Name} » ({entry.Source}) " +
                $"{(enabled ? "activée" : "désactivée")} via StartupApproved.");
            return true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Security.SecurityException)
        {
            LoggingService.LogException(ex, "StartupManagerService.SetEntryEnabled");
            return false;
        }
    }

    private static bool IsRegistryEntryEnabled(string name)
    {
        try
        {
            using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);
            if (approved?.GetValue(name) is not byte[] value || value.Length == 0)
            {
                return true;
            }

            return value[0] % 2 == 0;
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "StartupManagerService.IsRegistryEntryEnabled");
            return true;
        }
    }

    private static bool IsFolderEntryEnabled(string fileName)
    {
        try
        {
            using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedFolderKeyPath);
            if (approved?.GetValue(fileName) is not byte[] value || value.Length == 0)
            {
                return true;
            }

            return value[0] % 2 == 0;
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "StartupManagerService.IsFolderEntryEnabled");
            return true;
        }
    }

    private static byte[] BuildApprovalValue(bool enabled)
    {
        byte[] value = new byte[12];
        value[0] = enabled ? (byte)0x02 : (byte)0x03;

        byte[] timestamp = BitConverter.GetBytes(DateTime.UtcNow.ToFileTime());
        if (timestamp.Length == 8)
        {
            Array.Copy(timestamp, 0, value, 4, 8);
        }

        return value;
    }
}
