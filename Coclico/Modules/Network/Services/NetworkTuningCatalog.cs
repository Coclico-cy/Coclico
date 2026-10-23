using Coclico.Models.Network;

namespace Coclico.Services.Network;

public static class NetworkTuningCatalog
{
    public const string AutoTuningId = "tcp-autotuning";
    public const string ScalingHeuristicsId = "tcp-scalingheuristics";
    public const string EcnCapabilityId = "tcp-ecncapability";
    public const string TimestampsId = "tcp-timestamps";
    public const string RssId = "tcp-rss";
    public const string AdapterPowerId = "adapter-powermanagement";
    public const string AdapterEeeId = "adapter-eee";
    public const string AdapterFlowControlId = "adapter-flowcontrol";
    public const string AdapterInterruptModerationId = "adapter-interruptmoderation";

    public const string AutoTuningTarget = "normal";
    public const string ScalingHeuristicsTarget = "disabled";
    public const string EcnCapabilityTarget = "disabled";
    public const string TimestampsTarget = "disabled";
    public const string RssTarget = "enabled";
    public const string AdapterPowerTarget = "Disabled";
    public const string AdapterEeeTarget = "0";
    public const string AdapterFlowControlTarget = "0";
    public const string AdapterInterruptModerationTarget = "0";

    public static IReadOnlyList<NetworkTuningParameter> Definitions { get; } =
    [
        new NetworkTuningParameter
        {
            Id = AutoTuningId,
            NameKey = "Network_ParamAutoTuning",
            DescriptionKey = "Network_ParamAutoTuningDesc",
            Category = NetworkTuningCategory.TcpStack,
            TargetValue = AutoTuningTarget
        },
        new NetworkTuningParameter
        {
            Id = ScalingHeuristicsId,
            NameKey = "Network_ParamHeuristics",
            DescriptionKey = "Network_ParamHeuristicsDesc",
            Category = NetworkTuningCategory.TcpStack,
            TargetValue = ScalingHeuristicsTarget
        },
        new NetworkTuningParameter
        {
            Id = EcnCapabilityId,
            NameKey = "Network_ParamEcn",
            DescriptionKey = "Network_ParamEcnDesc",
            Category = NetworkTuningCategory.TcpStack,
            TargetValue = EcnCapabilityTarget
        },
        new NetworkTuningParameter
        {
            Id = TimestampsId,
            NameKey = "Network_ParamTimestamps",
            DescriptionKey = "Network_ParamTimestampsDesc",
            Category = NetworkTuningCategory.TcpStack,
            TargetValue = TimestampsTarget
        },
        new NetworkTuningParameter
        {
            Id = RssId,
            NameKey = "Network_ParamRss",
            DescriptionKey = "Network_ParamRssDesc",
            Category = NetworkTuningCategory.TcpStack,
            TargetValue = RssTarget
        },
        new NetworkTuningParameter
        {
            Id = AdapterPowerId,
            NameKey = "Network_ParamPowerManagement",
            DescriptionKey = "Network_ParamPowerManagementDesc",
            Category = NetworkTuningCategory.Adapter,
            TargetValue = AdapterPowerTarget
        },
        new NetworkTuningParameter
        {
            Id = AdapterEeeId,
            NameKey = "Network_ParamEee",
            DescriptionKey = "Network_ParamEeeDesc",
            Category = NetworkTuningCategory.Adapter,
            TargetValue = AdapterEeeTarget
        },
        new NetworkTuningParameter
        {
            Id = AdapterFlowControlId,
            NameKey = "Network_ParamFlowControl",
            DescriptionKey = "Network_ParamFlowControlDesc",
            Category = NetworkTuningCategory.Adapter,
            TargetValue = AdapterFlowControlTarget
        },
        new NetworkTuningParameter
        {
            Id = AdapterInterruptModerationId,
            NameKey = "Network_ParamInterruptModeration",
            DescriptionKey = "Network_ParamInterruptModerationDesc",
            Category = NetworkTuningCategory.Adapter,
            TargetValue = AdapterInterruptModerationTarget
        }
    ];

    public static NetworkTuningParameter? Find(string id)
    {
        foreach (NetworkTuningParameter p in Definitions)
        {
            if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }
        }

        return null;
    }
}
