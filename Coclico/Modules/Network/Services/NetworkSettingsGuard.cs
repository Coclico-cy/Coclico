using System.Collections.Frozen;
using Coclico.Models.Network;

namespace Coclico.Services.Network;

public static class NetworkSettingsGuard
{
    private static readonly FrozenSet<string> AutoTuningLevels = new[]
    {
        "disabled", "highlyrestricted", "restricted", "normal", "experimental"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> OnOffStates = new[]
    {
        "enabled", "disabled"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> CongestionProviders = new[]
    {
        "none", "newreno", "ctcp"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly char[] AdapterNameForbiddenChars =
    [
        '"', '\'', ';', '|', '&', '$', '`', '%', '\\', '<', '>', '\0', '\r', '\n'
    ];
    private static readonly char[] AdapterIdForbiddenChars =
    [
        '"', '\'', ';', '|', '&', '$', '`', '%', '\\', '/', '<', '>', '\0', '\r', '\n'
    ];

    public static bool IsValidTcpSettings(TcpGlobalSettings? settings)
    {
        if (settings is null)
        {
            LoggingService.LogWarning("[NetworkSettingsGuard] TcpGlobalSettings null - application refusée.");
            return false;
        }

        bool valid =
            AutoTuningLevels.Contains(settings.AutoTuningLevel) &&
            OnOffStates.Contains(settings.Rss) &&
            OnOffStates.Contains(settings.Rsc) &&
            OnOffStates.Contains(settings.EcnCapability) &&
            OnOffStates.Contains(settings.Timestamps) &&
            CongestionProviders.Contains(settings.CongestionProvider);

        if (!valid)
        {
            LoggingService.LogWarning(
                "[NetworkSettingsGuard] Réglages TCP hors liste blanche - application refusée. " +
                $"autotuninglevel='{settings.AutoTuningLevel}' rss='{settings.Rss}' rsc='{settings.Rsc}' " +
                $"ecncapability='{settings.EcnCapability}' timestamps='{settings.Timestamps}' " +
                $"congestionprovider='{settings.CongestionProvider}'");
        }

        return valid;
    }

    public static bool IsSafeAdapterName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name) &&
               name.Length <= 256 &&
               !name.AsSpan().ContainsAny(AdapterNameForbiddenChars);
    }

    public static bool IsSafeAdapterId(string? id)
    {
        return !string.IsNullOrWhiteSpace(id) &&
               id.Length <= 256 &&
               !id.AsSpan().ContainsAny(AdapterIdForbiddenChars);
    }

    public static string EscapePowerShellSingleQuoted(string value)
    {
        return value.Replace("'", "''");
    }
}
