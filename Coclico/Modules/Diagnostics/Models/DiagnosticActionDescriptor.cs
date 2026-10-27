using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Coclico.Modules.Diagnostics.Models;

public enum DiagnosticActionRisk
{
    Low,
    Medium,
    High
}

public sealed record DiagnosticActionDescriptor
{
    public required string ActionId { get; init; }

    public required string TitleKey { get; init; }

    public string? DescriptionKey { get; init; }

    public DiagnosticActionRisk Risk { get; init; } = DiagnosticActionRisk.Low;

    public bool RequiresElevation { get; init; }

    public bool RequiresConfirmation { get; init; } = true;

    public bool SupportsRollback { get; init; }

    public string? NavigationTag { get; init; }
}

public static class DiagnosticActionCatalog
{
    public const string OpenDiskAnalyzer = "open_disk_analyzer";

    public const string OpenNetwork = "open_network";

    public const string OpenRamCleaner = "open_ram_cleaner";

    public const string OpenPower = "open_power_optimizer";

    public const string OpenHealthDefense = "open_health_defense";

    public const string OpenPrograms = "open_programs";

    public const string OpenDeviceManager = "open_device_manager";

    private static readonly Dictionary<string, DiagnosticActionDescriptor> Actions = new(StringComparer.Ordinal)
    {
        [OpenDiskAnalyzer] = new DiagnosticActionDescriptor
        {
            ActionId = OpenDiskAnalyzer,
            TitleKey = "Diag_Action_OpenDiskAnalyzer",
            NavigationTag = "DiskAnalyzer",
            Risk = DiagnosticActionRisk.Low
        },
        [OpenNetwork] = new DiagnosticActionDescriptor
        {
            ActionId = OpenNetwork,
            TitleKey = "Diag_Action_OpenNetwork",
            NavigationTag = "Network",
            Risk = DiagnosticActionRisk.Low
        },
        [OpenRamCleaner] = new DiagnosticActionDescriptor
        {
            ActionId = OpenRamCleaner,
            TitleKey = "Diag_Action_OpenRamCleaner",
            NavigationTag = "RamCleaner",
            Risk = DiagnosticActionRisk.Low
        },
        [OpenPower] = new DiagnosticActionDescriptor
        {
            ActionId = OpenPower,
            TitleKey = "Diag_Action_OpenPower",
            NavigationTag = "PowerOptimizer",
            Risk = DiagnosticActionRisk.Low
        },
        [OpenHealthDefense] = new DiagnosticActionDescriptor
        {
            ActionId = OpenHealthDefense,
            TitleKey = "Diag_Action_OpenHealthDefense",
            NavigationTag = "HealthDefense",
            Risk = DiagnosticActionRisk.Low
        },
        [OpenPrograms] = new DiagnosticActionDescriptor
        {
            ActionId = OpenPrograms,
            TitleKey = "Diag_Action_OpenPrograms",
            NavigationTag = "Programs",
            Risk = DiagnosticActionRisk.Low
        },
        [OpenDeviceManager] = new DiagnosticActionDescriptor
        {
            ActionId = OpenDeviceManager,
            TitleKey = "Diag_Action_OpenDeviceManager",
            NavigationTag = "HealthDefense",
            Risk = DiagnosticActionRisk.Low
        }
    };

    public static IReadOnlyCollection<DiagnosticActionDescriptor> All => Actions.Values;

    public static bool TryGet(string actionId, [NotNullWhen(true)] out DiagnosticActionDescriptor? descriptor)
        => Actions.TryGetValue(actionId, out descriptor);

    public static bool IsKnown(string actionId) => Actions.ContainsKey(actionId);

    [Conditional("DEBUG")]
    internal static void ValidateCatalogIntegrity()
    {
        foreach (DiagnosticActionDescriptor descriptor in Actions.Values)
        {
            Debug.Assert(descriptor.RequiresConfirmation, "Every diagnostic action requires a confirmation.");
        }
    }
}
