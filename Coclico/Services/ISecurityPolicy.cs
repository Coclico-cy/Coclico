#nullable enable
using System.Threading.Tasks;

namespace Coclico.Services;

public interface ISecurityPolicy
{
    bool IsCommandBlocked(string normalizedCmd);

    bool IsPowerShellBlocked(string normalizedPs);

    bool IsProtectedPath(string lowerCasePath);

    string? PolicyFilePath { get; }

    Task ReloadAsync();
}
