using System.Collections.Frozen;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Coclico.Services;

public sealed class SecurityPolicyModel
{
    [JsonPropertyName("blockedCommandPatterns")]
    public List<string> BlockedCommandPatterns { get; set; } = [];

    [JsonPropertyName("blockedPowerShellPatterns")]
    public List<string> BlockedPowerShellPatterns { get; set; } = [];

    [JsonPropertyName("blockedPowerShellWildcards")]
    public List<string> BlockedPowerShellWildcards { get; set; } = [];

    [JsonPropertyName("protectedPathSegments")]
    public List<string> ProtectedPathSegments { get; set; } = [];
}

public sealed class SecurityPolicyService : ISecurityPolicy
{
    private static readonly string PolicyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Coclico", "security-policy.json");

    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly IReadOnlyList<string> _defaultCmdPatterns =
    [
        "format ", "format/", "rd /s /q", "rmdir /s /q",
        "del /f /s /q", "del /s /q", "rm -rf", "rm -r /",
        "cipher /w:", "bcdedit", "diskpart",
        "reg delete hklm", "reg delete hkcc",
        "net user", "net localgroup administrators",
        "icacls", "takeown", "cacls", "sc delete", "sc stop",
    ];

    private static readonly IReadOnlyList<string> _defaultPsPatterns =
    [
        "format-volume", "clear-disk", "initialize-disk", "set-partition",
        "remove-item -recurse", "remove-item -r ", "ri -recurse",
        "invoke-expression", "iex ", "iex(",
        "-encodedcommand", "-enc ",
        "[convert]::frombase64string", "::frombase64string",
        "downloadstring", "downloadfile",
        "invoke-webrequest", "iwr ",
        "new-object net.webclient", "[net.webclient]",
        "[system.reflection.assembly]::load", "[reflection.assembly]::load",
        "assembly::loadfrom", "assembly::loadfile",
        "set-mppreference", "add-mppreference",
        "disable-windowsoptionalfeature",
        "net user", "net localgroup", "add-localgroup",
        "new-localuser", "remove-itemproperty",
    ];

    private static readonly IReadOnlyList<string> _defaultPsWildcards =
    [
        @"set-itemproperty.*hklm",
    ];

    private static readonly IReadOnlyList<string> _defaultProtectedPaths =
    [
        @"\windows\", @"\program files\", @"\program files (x86)\",
        @"\programdata\microsoft\", @"\system volume information\",
        @"\$recycle.bin\", @"\recovery\", @"\boot\", @"\efi\",
    ];

    private static readonly string[] _approvedExecutableRoots =
    [
        Environment.SystemDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    ];

    private static readonly Dictionary<string, string> _standardSystemApps = new(StringComparer.OrdinalIgnoreCase)
    {
        { "notepad", "notepad.exe" },
        { "notepad.exe", "notepad.exe" },
        { "calc", "calc.exe" },
        { "calc.exe", "calc.exe" },
        { "calculator", "calc.exe" },
        { "paint", "mspaint.exe" },
        { "mspaint", "mspaint.exe" },
        { "mspaint.exe", "mspaint.exe" },
        { "snippingtool", "snippingtool.exe" },
        { "snippingtool.exe", "snippingtool.exe" },
        { "write", "write.exe" },
        { "write.exe", "write.exe" },
        { "wordpad", "write.exe" },
        { "taskmgr", "taskmgr.exe" },
        { "taskmgr.exe", "taskmgr.exe" },
        { "control", "control.exe" },
        { "control.exe", "control.exe" },
        { "explorer", "explorer.exe" },
        { "explorer.exe", "explorer.exe" },
        { "powershell", "powershell.exe" },
        { "powershell.exe", "powershell.exe" },
        { "pwsh", "pwsh.exe" },
        { "pwsh.exe", "pwsh.exe" },
        { "cleanmgr", "cleanmgr.exe" },
        { "cleanmgr.exe", "cleanmgr.exe" },
        { "cmd", "cmd.exe" },
        { "cmd.exe", "cmd.exe" },
        { "msconfig", "msconfig.exe" },
        { "msconfig.exe", "msconfig.exe" },
        { "resmon", "resmon.exe" },
        { "resmon.exe", "resmon.exe" },
    };

    private FrozenSet<string> _cmdPatterns = FrozenSet<string>.Empty;
    private FrozenSet<string> _psPatterns = FrozenSet<string>.Empty;
    private FrozenSet<string> _protectedPaths = FrozenSet<string>.Empty;
    private Regex[] _psWildcards = [];

    public string? PolicyFilePath { get; private set; }

    public SecurityPolicyService()
    {
        BuildFromDefaults();
        if (File.Exists(PolicyPath))
        {
            MergeFromFile(PolicyPath);
        }
    }

    public bool IsCommandBlocked(string normalizedCmd)
    {
        foreach (string p in _cmdPatterns)
        {
            if (normalizedCmd.Contains(p, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsPowerShellBlocked(string normalizedPs)
    {
        foreach (string p in _psPatterns)
        {
            if (normalizedPs.Contains(p, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (Regex rx in _psWildcards)
        {
            if (rx.IsMatch(normalizedPs))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsProtectedPath(string lowerCasePath)
    {
        foreach (string seg in _protectedPaths)
        {
            if (lowerCasePath.Contains(seg, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsApplicationAllowed(string appName, out string? resolvedPath)
    {
        resolvedPath = null;
        if (string.IsNullOrWhiteSpace(appName))
        {
            return false;
        }

        string trimmed = appName.Trim().Trim('"', '\'');

        if (IsCommandBlocked(trimmed.ToLowerInvariant()))
        {
            return false;
        }

        if (trimmed.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
        {
            string page = trimmed["ms-settings:".Length..];
            if (page.Length > 0 && page.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                resolvedPath = trimmed;
                return true;
            }

            LoggingService.LogWarning($"[SecurityPolicy] URI ms-settings malformée refusée : {trimmed}");
            return false;
        }

        if (_standardSystemApps.TryGetValue(trimmed, out string? exeName))
        {
            resolvedPath = Path.Combine(Environment.SystemDirectory, exeName);
            if (File.Exists(resolvedPath))
            {
                return true;
            }

            resolvedPath = exeName;
            return true;
        }

        if (File.Exists(trimmed))
        {
            string ext = Path.GetExtension(trimmed).ToLowerInvariant();
            if (ext == ".exe")
            {
                string fullPath = Path.GetFullPath(trimmed);
                if (IsApprovedExecutableLocation(fullPath))
                {
                    resolvedPath = fullPath;
                    return true;
                }

                LoggingService.LogWarning($"[SecurityPolicy] Exécutable hors emplacements approuvés refusé : {fullPath}");
                return false;
            }
        }

        return false;
    }

    private static bool IsApprovedExecutableLocation(string fullPath)
    {
        foreach (string root in _approvedExecutableRoots)
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            if (fullPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public async Task ReloadAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            BuildFromDefaults();
            if (File.Exists(PolicyPath))
            {
                MergeFromFile(PolicyPath);
            }

            LoggingService.LogInfo($"[SecurityPolicy] Rechargé — {_cmdPatterns.Count} cmd, " +
                                   $"{_psPatterns.Count} ps patterns.");
        }
        finally { _ = _lock.Release(); }
    }

    private void BuildFromDefaults()
    {
        _cmdPatterns = _defaultCmdPatterns.ToFrozenSet(StringComparer.Ordinal);
        _psPatterns = _defaultPsPatterns.ToFrozenSet(StringComparer.Ordinal);
        _protectedPaths = _defaultProtectedPaths.ToFrozenSet(StringComparer.Ordinal);
        _psWildcards = CompileWildcards(_defaultPsWildcards);
        PolicyFilePath = null;
    }

    private void MergeFromFile(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            SecurityPolicyModel? model = JsonSerializer.Deserialize<SecurityPolicyModel>(json);
            if (model is null)
            {
                return;
            }

            var merged = new HashSet<string>(_defaultCmdPatterns, StringComparer.Ordinal);
            merged.UnionWith(model.BlockedCommandPatterns);
            _cmdPatterns = merged.ToFrozenSet(StringComparer.Ordinal);

            var mergedPs = new HashSet<string>(_defaultPsPatterns, StringComparer.Ordinal);
            mergedPs.UnionWith(model.BlockedPowerShellPatterns);
            _psPatterns = mergedPs.ToFrozenSet(StringComparer.Ordinal);

            var mergedPaths = new HashSet<string>(_defaultProtectedPaths, StringComparer.Ordinal);
            mergedPaths.UnionWith(model.ProtectedPathSegments);
            _protectedPaths = mergedPaths.ToFrozenSet(StringComparer.Ordinal);

            var allWildcards = new List<string>(_defaultPsWildcards);
            allWildcards.AddRange(model.BlockedPowerShellWildcards);
            _psWildcards = CompileWildcards(allWildcards);

            PolicyFilePath = path;
            LoggingService.LogInfo(
                $"[SecurityPolicy] Politique enterprise chargée : {path} " +
                $"(+{model.BlockedCommandPatterns.Count} cmd, " +
                $"+{model.BlockedPowerShellPatterns.Count} ps, " +
                $"+{model.ProtectedPathSegments.Count} paths)");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "SecurityPolicyService.MergeFromFileAsync");
        }
    }

    private static Regex[] CompileWildcards(IReadOnlyList<string> patterns)
    {
        var result = new List<Regex>(patterns.Count);
        foreach (string p in patterns)
        {
            try
            {
                result.Add(new Regex(p,
                    RegexOptions.Compiled | RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(100)));
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, $"SecurityPolicy.CompileWildcard({p})");
            }
        }
        return [.. result];
    }
}