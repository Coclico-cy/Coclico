namespace Coclico.Services;

public interface ISecurityPolicy
{
    bool IsCommandBlocked(string normalizedCmd);

    bool IsPowerShellBlocked(string normalizedPs);

    bool IsProtectedPath(string lowerCasePath);

    bool IsApplicationAllowed(string appName, out string? resolvedPath);

    string? PolicyFilePath { get; }

    Task ReloadAsync();
}
