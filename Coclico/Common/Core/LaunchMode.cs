namespace Coclico.Services;

public enum LaunchMode { Normal, Minimized, Tray, Maximized }

public static class LaunchModes
{
    public static bool IsValid(string? value)
    {
        return Enum.TryParse<LaunchMode>(value, ignoreCase: true, out LaunchMode mode)
            && Enum.IsDefined(mode);
    }

    public static LaunchMode Parse(string? value)
    {
        if (Enum.TryParse<LaunchMode>(value, ignoreCase: true, out LaunchMode mode)
            && Enum.IsDefined(mode))
        {
            return mode;
        }

        return LaunchMode.Normal;
    }
}
