namespace Coclico.Security.Domain;

public sealed class UpdatePolicy
{
    public string MinimumAllowedVersion { get; set; } = string.Empty;

    public string LastKnownGoodVersion { get; set; } = string.Empty;

    public int MinimumSecurityGeneration { get; set; } = 1;
}
