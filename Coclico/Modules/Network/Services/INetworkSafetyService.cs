using Coclico.Models.Network;

namespace Coclico.Services.Network;

public interface INetworkSafetyService
{
    void ClassifyParameter(DynamicNetworkParameter parameter);
    bool IsAllowedInMode(DynamicNetworkParameter parameter, EngineExecutionMode mode);
    bool ValidateProposedValue(DynamicNetworkParameter parameter, string proposedValue, out string failureReason);
    string GetExplanationForParameter(DynamicNetworkParameter parameter);
}
