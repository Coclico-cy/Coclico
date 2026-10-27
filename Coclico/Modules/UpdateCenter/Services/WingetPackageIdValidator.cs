using System.Text.RegularExpressions;

namespace Coclico.Modules.UpdateCenter.Services;

public static partial class WingetPackageIdValidator
{
    [GeneratedRegex("^[A-Za-z0-9.\\-_]+$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? packageId)
        => !string.IsNullOrWhiteSpace(packageId) && Pattern().IsMatch(packageId);
}
