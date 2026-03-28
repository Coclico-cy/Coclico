#nullable enable
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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

    private FrozenSet<string> _cmdPatterns = FrozenSet<string>.Empty;
    private FrozenSet<string> _psPatterns = FrozenSet<string>.Empty;
    private FrozenSet<string> _protectedPaths = FrozenSet<string>.Empty;
    private Regex[] _psWildcards = [];

    public string? PolicyFilePath { get; private set; }

    private readonly Task _initTask;

    public SecurityPolicyService()
    {
        BuildFromDefaults();
        _initTask = File.Exists(PolicyPath)
            ? MergeFromFileAsync(PolicyPath)
            : Task.CompletedTask;
    }

    private Task EnsureInitializedAsync() => _initTask;

    public bool IsCommandBlocked(string normalizedCmd)
    {
        foreach (var p in _cmdPatterns)
            if (normalizedCmd.Contains(p, StringComparison.Ordinal)) return true;
        return false;
    }

    public bool IsPowerShellBlocked(string normalizedPs)
    {
        foreach (var p in _psPatterns)
            if (normalizedPs.Contains(p, StringComparison.Ordinal)) return true;

        foreach (var rx in _psWildcards)
            if (rx.IsMatch(normalizedPs)) return true;

        return false;
    }

    public bool IsProtectedPath(string lowerCasePath)
    {
        foreach (var seg in _protectedPaths)
            if (lowerCasePath.Contains(seg, StringComparison.Ordinal)) return true;
        return false;
    }

    public async Task ReloadAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            BuildFromDefaults();
            if (File.Exists(PolicyPath))
                await MergeFromFileAsync(PolicyPath).ConfigureAwait(false);

            LoggingService.LogInfo($"[SecurityPolicy] Rechargé — {_cmdPatterns.Count} cmd, " +
                                   $"{_psPatterns.Count} ps patterns.");
        }
        finally { _lock.Release(); }
    }

    private void BuildFromDefaults()
    {
        _cmdPatterns = _defaultCmdPatterns.ToFrozenSet(StringComparer.Ordinal);
        _psPatterns = _defaultPsPatterns.ToFrozenSet(StringComparer.Ordinal);
        _protectedPaths = _defaultProtectedPaths.ToFrozenSet(StringComparer.Ordinal);
        _psWildcards = CompileWildcards(_defaultPsWildcards);
        PolicyFilePath = null;
    }

    private async Task MergeFromFileAsync(string path)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            var model = JsonSerializer.Deserialize<SecurityPolicyModel>(json);
            if (model is null) return;

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
        foreach (var p in patterns)
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
