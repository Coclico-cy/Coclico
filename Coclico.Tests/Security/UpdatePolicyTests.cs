using Coclico.Security.Domain;
using Coclico.Security.Infrastructure;
using Xunit;

namespace Coclico.Tests;

public sealed class UpdatePolicyTests
{
    [Fact]
    public void NewerGenerationIsAllowed()
    {
        var policy = new UpdatePolicy { MinimumAllowedVersion = "2.2.2", MinimumSecurityGeneration = 1 };
        UpdatePolicyResult result = UpdatePolicyChecker.Evaluate("2.2.2", 2, policy);
        Assert.Equal(UpdatePolicyDecision.Allowed, result.Decision);
    }

    [Fact]
    public void OldVersionIsBlocked()
    {
        var policy = new UpdatePolicy { MinimumAllowedVersion = "2.2.2", MinimumSecurityGeneration = 1 };
        UpdatePolicyResult result = UpdatePolicyChecker.Evaluate("2.0.0", 2, policy);
        Assert.Equal(UpdatePolicyDecision.Blocked, result.Decision);
    }

    [Fact]
    public void OldGenerationIsBlocked()
    {
        var policy = new UpdatePolicy { MinimumAllowedVersion = "2.2.2", MinimumSecurityGeneration = 3 };
        UpdatePolicyResult result = UpdatePolicyChecker.Evaluate("2.2.2", 1, policy);
        Assert.Equal(UpdatePolicyDecision.Blocked, result.Decision);
    }

    [Fact]
    public void KnownGoodVersionIsRecovery()
    {
        var policy = new UpdatePolicy { MinimumAllowedVersion = "2.2.2", LastKnownGoodVersion = "2.2.2", MinimumSecurityGeneration = 2 };
        UpdatePolicyResult result = UpdatePolicyChecker.Evaluate("2.2.2", 1, policy);
        Assert.Equal(UpdatePolicyDecision.RecoveryAllowed, result.Decision);
    }
}
