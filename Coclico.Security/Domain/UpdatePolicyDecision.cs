namespace Coclico.Security.Domain;

public enum UpdatePolicyDecision
{
    Allowed,
    RecoveryAllowed,
    Blocked
}

public sealed class UpdatePolicyResult
{
    public UpdatePolicyDecision Decision { get; init; } = UpdatePolicyDecision.Blocked;

    public string Reason { get; init; } = string.Empty;

    public static UpdatePolicyResult Allowed()
    {
        return new UpdatePolicyResult { Decision = UpdatePolicyDecision.Allowed, Reason = string.Empty };
    }

    public static UpdatePolicyResult RecoveryAllowed(string reason)
    {
        return new UpdatePolicyResult { Decision = UpdatePolicyDecision.RecoveryAllowed, Reason = reason };
    }

    public static UpdatePolicyResult Blocked(string reason)
    {
        return new UpdatePolicyResult { Decision = UpdatePolicyDecision.Blocked, Reason = reason };
    }
}
