using System;
using System.IO;
using Microsoft.Win32;

namespace Coclico.Installer.Services;

public record SystemRequirementStatus(
    bool HasDotNet10,
    string DotNet10Version,
    bool HasNvidiaGpu,
    string GpuName,
    long AvailableDiskSpaceBytes,
    string TargetDriveLetter);

public static class SystemRequirementService
{
    public static SystemRequirementStatus CheckRequirements(string targetPath)
    {
        bool hasDotNet10 = false;
        string dotNetVersion = "Non détecté";

        try
        {
            string defaultDotNetDir = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet", "shared", "Microsoft.WindowsDesktop.App");

            if (Directory.Exists(defaultDotNetDir))
            {
                foreach (string dirName in Directory.EnumerateDirectories(defaultDotNetDir).Select(dir => Path.GetFileName(dir) ?? string.Empty))
                {
                    if (dirName.StartsWith("10.", StringComparison.OrdinalIgnoreCase))
                    {
                        hasDotNet10 = true;
                        dotNetVersion = $"v{dirName}";
                        break;
                    }
                }
            }

            if (!hasDotNet10)
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App");
                if (key != null)
                {
                    foreach (string valName in key.GetValueNames())
                    {
                        if (valName.StartsWith("10.", StringComparison.OrdinalIgnoreCase))
                        {
                            hasDotNet10 = true;
                            dotNetVersion = $"v{valName}";
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Security.SecurityException) { _ = ex; }

        var gpu = GpuDetector.DetectGpu();

        long freeBytes = 0;
        string driveLetter = "C:";
        try
        {
            string root = Path.GetPathRoot(targetPath) ?? "C:\\";
            driveLetter = root.TrimEnd('\\');
            var driveInfo = new DriveInfo(root);
            if (driveInfo.IsReady)
            {
                freeBytes = driveInfo.AvailableFreeSpace;
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }

        return new SystemRequirementStatus(
            hasDotNet10,
            dotNetVersion,
            gpu.HasNvidia,
            gpu.Name,
            freeBytes,
            driveLetter);
    }
}
