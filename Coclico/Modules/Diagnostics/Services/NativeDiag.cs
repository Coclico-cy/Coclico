namespace Coclico.Modules.Diagnostics.Services;

using System.Runtime.InteropServices;

internal static class NativeDiag
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved1;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    public static bool HasBattery()
    {
        if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
        {
            return false;
        }

        return status.ACLineStatus is not 255 && status.BatteryFlag is not 128 and not 255;
    }

    public static bool IsOnAcPower()
    {
        if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
        {
            return true;
        }

        return status.ACLineStatus == 1;
    }

    public static int? GetChargePercent()
    {
        if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
        {
            return null;
        }

        return status.BatteryLifePercent is <= 100 and not 255
            ? status.BatteryLifePercent
            : null;
    }

    public static int? GetEstimatedRuntimeMinutes()
    {
        if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
        {
            return null;
        }

        return status.BatteryLifeTime > 0 ? status.BatteryLifeTime / 60 : null;
    }

    public static double? GetFullLifeRatio()
    {
        if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
        {
            return null;
        }

        if (status.BatteryFullLifeTime <= 0 || status.BatteryLifeTime <= 0)
        {
            return null;
        }

        return (double)status.BatteryLifeTime / status.BatteryFullLifeTime;
    }
}
