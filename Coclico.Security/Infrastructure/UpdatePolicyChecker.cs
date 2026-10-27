using Coclico.Security.Domain;

namespace Coclico.Security.Infrastructure;

public static class UpdatePolicyChecker
{
    public static UpdatePolicyResult Evaluate(string proposedVersion, int proposedSecurityGeneration, UpdatePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (TryParse(proposedVersion, out Version? proposed) && TryParse(policy.LastKnownGoodVersion, out Version? knownGood) &&
            proposed is not null && knownGood is not null && proposed.Equals(knownGood))
        {
            return UpdatePolicyResult.RecoveryAllowed("target version is the last known good version");
        }

        if (TryParse(policy.MinimumAllowedVersion, out Version? minimum) &&
            TryParse(proposedVersion, out Version? candidate) &&
            candidate < minimum)
        {
            return UpdatePolicyResult.Blocked($"version {proposedVersion} is below the minimum allowed version {policy.MinimumAllowedVersion}");
        }

        if (proposedSecurityGeneration < policy.MinimumSecurityGeneration)
        {
            return UpdatePolicyResult.Blocked($"security generation {proposedSecurityGeneration} is below the minimum {policy.MinimumSecurityGeneration}");
        }

        return UpdatePolicyResult.Allowed();
    }

    private static bool TryParse(string version, out Version? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        string normalized = version.Trim().TrimStart('v', 'V');
        int plus = normalized.IndexOf('+');
        if (plus >= 0)
        {
            normalized = normalized[..plus];
        }

        return Version.TryParse(normalized, out parsed);
    }
}
