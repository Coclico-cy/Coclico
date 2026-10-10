using System;
using System.IO;

namespace Coclico.Installer.Models;

public class InstallConfig
{
    public static string DefaultInstallPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "Coclico");

    public string InstallPath { get; set; } = DefaultInstallPath;
    public bool CreateDesktopShortcut { get; set; } = true;
    public bool CreateStartMenuShortcut { get; set; } = true;
    public bool AutoLaunchOnFinish { get; set; } = true;

    public bool InstallCuda { get; set; } = false;
    public bool InstallAiModel { get; set; } = false;
    public InstallerAiModel SelectedAiModel { get; set; } = InstallerAiCatalog.Models[0];

    public bool HasNvidiaGpu { get; set; } = false;
    public string GpuName { get; set; } = string.Empty;
    public bool HasDotNet10 { get; set; } = false;
    public string DotNetVersion { get; set; } = string.Empty;
    public long AvailableDiskSpaceBytes { get; set; } = 0;
    public string TargetDriveLetter { get; set; } = "C:";

    public int CalculateRequiredSpaceMb()
    {
        int baseMb = 350;
        if (InstallCuda) baseMb += 1150;
        if (InstallAiModel && SelectedAiModel != null) baseMb += (int)SelectedAiModel.SizeMb;
        return baseMb;
    }
}