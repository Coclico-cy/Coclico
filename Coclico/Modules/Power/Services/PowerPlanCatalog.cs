using System.Globalization;

namespace Coclico.Modules.Power.Services;

public enum CoclicoPowerMode
{
    Economy,
    Optimal,
    Performance
}

public sealed record PowerSettingOverride(
    string SubGroupGuid,
    string SettingGuid,
    string Alias,
    uint AcValue,
    uint DcValue);

public sealed record PowerPlanDefinition(
    CoclicoPowerMode Mode,
    string PlanName,
    string Description,
    string BaseSchemeGuid,
    IReadOnlyList<PowerSettingOverride> Settings);

public static class PowerPlanCatalog
{
    public const string BasePowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";
    public const string BaseBalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string BaseHighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    public const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    public const string SubUsb = "2a737441-1930-4402-8d77-b2bebba308a3";
    public const string SubWireless = "19cbb8fa-5279-450e-9fac-8a3d5fedd0c1";
    public const string SubPciExpress = "501a4d13-42af-4429-9fd1-a8218c268e20";
    public const string SubVideo = "7516b95f-f776-4464-8c53-06167f40cc99";
    public const string SubDisk = "0012ee47-9041-4b5d-9b77-535fba8b1442";
    public const string SubSleep = "238c9fa8-0aad-41ed-83f4-97be242c8f20";

    public const string ProcThrottleMin = "893dee8e-2bef-41e0-89c6-b55d0929964c";
    public const string ProcThrottleMax = "bc5038f7-23e0-4960-96da-33abaf5935ec";
    public const string PerfBoostMode = "be337238-0d82-4146-a960-4f3749d470c7";
    public const string PerfBoostPolicy = "45bcc044-d885-43e2-8605-ee0ec6e96b59";
    public const string PerfEpp = "36687f9e-e3a5-4dbf-b1dc-15eb381c6863";
    public const string SchedPolicy = "93b8b6dc-0698-4d1c-9ee4-0644e900c85d";
    public const string SysCoolPolicy = "94d3a615-a899-4ac5-ae2b-e4d8f634367f";
    public const string CpMinCores = "0cc5b647-c1df-4637-891a-dec35c318583";
    public const string UsbSelectiveSuspend = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
    public const string Usb3LinkPowerManagement = "d4e98f31-5ffe-4ce1-be31-1b38b384c009";
    public const string WirelessPowerMode = "12bbebe6-58d6-4636-95bb-3217ef867c1a";
    public const string LinkStatePowerManagement = "ee12f906-d277-404b-b6da-e5fa1a576df5";
    public const string VideoIdle = "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e";
    public const string VideoDim = "17aaa29b-8b43-4b94-aafe-35f64daaf1ee";
    public const string DiskIdle = "6738e2c4-e8a5-4a42-b16a-e040e769756e";
    public const string AhciLinkPowerManagement = "0b2d69d7-a2a1-449c-9680-f91c70521c60";
    public const string StandbyIdle = "29f6c1db-86da-48c5-9fdb-f2b67b1f44da";

    public static string GetPlanName(CoclicoPowerMode mode) => mode switch
    {
        CoclicoPowerMode.Economy => "Coclico economie",
        CoclicoPowerMode.Optimal => "Coclico optimal",
        CoclicoPowerMode.Performance => "Coclico performance",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    public static string GetPlanDescription(CoclicoPowerMode mode) => mode switch
    {
        CoclicoPowerMode.Economy => "Autonomie maximale : boost econome, composants en economie d'energie.",
        CoclicoPowerMode.Optimal => "Equilibre : reactif sur secteur, econome sur batterie.",
        CoclicoPowerMode.Performance => "Reactivite maximale : boost agressif, composants toujours actifs.",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    public static string GetBaseSchemeGuid(CoclicoPowerMode mode) => mode switch
    {
        CoclicoPowerMode.Economy => BasePowerSaverGuid,
        CoclicoPowerMode.Optimal => BaseBalancedGuid,
        CoclicoPowerMode.Performance => BaseHighPerformanceGuid,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    public static IReadOnlyList<PowerSettingOverride> GetSettings(CoclicoPowerMode mode)
    {
        List<PowerSettingOverride> settings =
        [
            new(SubProcessor, ProcThrottleMin, "PROCTHROTTLEMIN", 0, 0),
            new(SubProcessor, ProcThrottleMax, "PROCTHROTTLEMAX", 100, 100),
            new(SubProcessor, PerfBoostMode, "PERFBOOSTMODE", 0, 0),
            new(SubProcessor, PerfBoostPolicy, "PERFBOOSTPOL", 0, 0),
            new(SubProcessor, PerfEpp, "PERFEPP", 0, 0),
            new(SubProcessor, SchedPolicy, "SCHEDPOLICY", 0, 0),
            new(SubProcessor, SysCoolPolicy, "SYSCOOLPOL", 1, 1),
            new(SubProcessor, CpMinCores, "CPMINCORES", 0, 0),

            new(SubUsb, UsbSelectiveSuspend, "USBSELECTIVE", 0, 0),
            new(SubUsb, Usb3LinkPowerManagement, "USB3LPM", 0, 0),
            new(SubWireless, WirelessPowerMode, "WIRELESSPOWER", 0, 0),
            new(SubPciExpress, LinkStatePowerManagement, "ASPM", 0, 0),

            new(SubVideo, VideoIdle, "VIDEOIDLE", 0, 0),
            new(SubVideo, VideoDim, "VIDEODIM", 0, 0),
            new(SubDisk, DiskIdle, "DISKIDLE", 0, 0),
            new(SubDisk, AhciLinkPowerManagement, "AHCILPM", 0, 0),
            new(SubSleep, StandbyIdle, "STANDBYIDLE", 0, 0)
        ];

        switch (mode)
        {
            case CoclicoPowerMode.Economy:
                Set(settings, ProcThrottleMin, 5, 5);
                Set(settings, PerfBoostMode, 3, 3);
                Set(settings, PerfBoostPolicy, 50, 50);
                Set(settings, PerfEpp, 100, 100);
                Set(settings, SchedPolicy, 3, 3);
                Set(settings, CpMinCores, 10, 10);
                Set(settings, UsbSelectiveSuspend, 1, 1);
                Set(settings, Usb3LinkPowerManagement, 3, 3);
                Set(settings, WirelessPowerMode, 3, 3);
                Set(settings, LinkStatePowerManagement, 2, 2);
                Set(settings, VideoIdle, 120, 60);
                Set(settings, VideoDim, 60, 30);
                Set(settings, DiskIdle, 120, 120);
                Set(settings, AhciLinkPowerManagement, 3, 3);
                Set(settings, StandbyIdle, 600, 600);
                break;
            case CoclicoPowerMode.Optimal:
                Set(settings, ProcThrottleMin, 10, 5);
                Set(settings, PerfBoostMode, 2, 1);
                Set(settings, PerfBoostPolicy, 100, 80);
                Set(settings, PerfEpp, 40, 60);
                Set(settings, SchedPolicy, 5, 5);
                Set(settings, CpMinCores, 0, 10);
                Set(settings, UsbSelectiveSuspend, 1, 1);
                Set(settings, Usb3LinkPowerManagement, 2, 2);
                Set(settings, WirelessPowerMode, 1, 2);
                Set(settings, LinkStatePowerManagement, 1, 1);
                Set(settings, VideoIdle, 0, 600);
                Set(settings, VideoDim, 0, 120);
                Set(settings, DiskIdle, 0, 300);
                Set(settings, AhciLinkPowerManagement, 0, 3);
                Set(settings, StandbyIdle, 0, 1800);
                break;
            case CoclicoPowerMode.Performance:
                Set(settings, ProcThrottleMin, 100, 100);
                Set(settings, PerfBoostMode, 2, 2);
                Set(settings, PerfBoostPolicy, 100, 100);
                Set(settings, PerfEpp, 0, 0);
                Set(settings, SchedPolicy, 1, 1);
                Set(settings, CpMinCores, 0, 0);
                break;
        }

        return settings;
    }

    private static void Set(List<PowerSettingOverride> settings, string settingGuid, uint acValue, uint dcValue)
    {
        int index = settings.FindIndex(s => s.SettingGuid == settingGuid);
        if (index < 0)
        {
            throw new InvalidOperationException($"Parametre inconnu dans le catalogue : {settingGuid}");
        }

        settings[index] = settings[index] with { AcValue = acValue, DcValue = dcValue };
    }

    public static IReadOnlyList<string> BuildSetIndexArguments(string schemeGuid, PowerSettingOverride setting, bool ac)
    {
        return
        [
            ac ? "/setacvalueindex" : "/setdcvalueindex",
            schemeGuid,
            setting.SubGroupGuid,
            setting.SettingGuid,
            (ac ? setting.AcValue : setting.DcValue).ToString(CultureInfo.InvariantCulture)
        ];
    }

    public static CoclicoPowerMode? TryGetModeFromPlanName(string? planName)
    {
        if (string.IsNullOrWhiteSpace(planName))
        {
            return null;
        }

        foreach (CoclicoPowerMode mode in Enum.GetValues<CoclicoPowerMode>())
        {
            if (string.Equals(planName.Trim(), GetPlanName(mode), StringComparison.OrdinalIgnoreCase))
            {
                return mode;
            }
        }

        return null;
    }
}