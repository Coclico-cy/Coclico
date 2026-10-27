using System.Text.RegularExpressions;
using Xunit;

namespace Coclico.Tests;

public sealed class DiagnosticResourceKeyTests
{
    private static readonly Regex KeyRegex = new("x:Key=\"([^\"]+)\"", RegexOptions.Compiled);

    private static readonly string[] CategoryKeys =
    [
        "Diag_Category_Windows",
        "Diag_Category_Cpu",
        "Diag_Category_Gpu",
        "Diag_Category_Memory",
        "Diag_Category_Storage",
        "Diag_Category_Network",
        "Diag_Category_Power",
        "Diag_Category_Devices",
        "Diag_Category_Processes",
        "Diag_Category_Security",
        "Diag_Category_Motherboard"
    ];

    private static readonly string[] SeverityKeys =
    [
        "Diag_Severity_Success",
        "Diag_Severity_Info",
        "Diag_Severity_Warning",
        "Diag_Severity_Error",
        "Diag_Severity_Critical"
    ];

    private static readonly string[] ConfidenceKeys =
    [
        "Diag_Confidence_Low",
        "Diag_Confidence_Medium",
        "Diag_Confidence_High"
    ];

    private static readonly string[] ValueStateKeys =
    [
        "Diag_ValueState_Measured",
        "Diag_ValueState_NotExposed",
        "Diag_ValueState_AccessDenied",
        "Diag_ValueState_SourceError",
        "Diag_ValueState_Stale",
        "Diag_ValueState_NotApplicable"
    ];

    private static readonly string[] ModeKeys =
    [
        "Diag_Mode_Quick",
        "Diag_Mode_Full",
        "Diag_Mode_Targeted"
    ];

    private static readonly string[] RunStatusKeys =
    [
        "Diag_RunStatus_Running",
        "Diag_RunStatus_Completed",
        "Diag_RunStatus_Partial",
        "Diag_RunStatus_Cancelled",
        "Diag_RunStatus_Failed"
    ];

    private static readonly string[] CheckKeys =
    [
        "Diag_Check_windows_health",
        "Diag_Check_cpu_info",
        "Diag_Check_gpu_info",
        "Diag_Check_memory_pressure",
        "Diag_Check_storage_space",
        "Diag_Check_network_probe",
        "Diag_Check_power_battery",
        "Diag_Check_devices_pnp",
        "Diag_Check_processes_startup",
        "Diag_Check_security_defender",
        "Diag_Check_motherboard_info"
    ];

    public static IEnumerable<object[]> AllDynamicKeys() =>
        CategoryKeys
            .Concat(SeverityKeys)
            .Concat(ConfidenceKeys)
            .Concat(ValueStateKeys)
            .Concat(ModeKeys)
            .Concat(RunStatusKeys)
            .Concat(CheckKeys)
            .Select(key => new object[] { key });

    private static HashSet<string> ExtractKeys(string path)
    {
        string content = File.ReadAllText(path);
        HashSet<string> keys = [];
        foreach (Match match in KeyRegex.Matches(content))
        {
            keys.Add(match.Groups[1].Value);
        }

        return keys;
    }

    [Theory]
    [MemberData(nameof(AllDynamicKeys))]
    public void DynamicKey_ExistsInBothLanguageFiles(string key)
    {
        string repoRoot = XamlResourceValidationTests.FindRepoRoot();
        HashSet<string> fr = ExtractKeys(Path.Join(repoRoot, "Coclico", "Resources", "Lang", "fr.xaml"));
        HashSet<string> en = ExtractKeys(Path.Join(repoRoot, "Coclico", "Resources", "Lang", "en.xaml"));

        Assert.True(fr.Contains(key), $"Clé absente de fr.xaml : {key}");
        Assert.True(en.Contains(key), $"Clé absente de en.xaml : {key}");
    }

    [Fact]
    public void EveryDiagnosticsEnumValue_HasACategoryKey()
    {
        string[] categories = Enum.GetNames(typeof(Coclico.Modules.Diagnostics.Models.DiagnosticCategory));
        string repoRoot = XamlResourceValidationTests.FindRepoRoot();
        HashSet<string> fr = ExtractKeys(Path.Join(repoRoot, "Coclico", "Resources", "Lang", "fr.xaml"));

        Assert.All(categories, category =>
            Assert.Contains($"Diag_Category_{category}", fr));
        Assert.Equal(categories.Length, CategoryKeys.Length);
    }

    [Fact]
    public void EveryRegisteredCheckId_HasACheckKey()
    {
        string[] checkIds =
        [
            "windows.health",
            "cpu.info",
            "gpu.info",
            "memory.pressure",
            "storage.space",
            "network.probe",
            "power.battery",
            "devices.pnp",
            "processes.startup",
            "security.defender",
            "motherboard.info"
        ];

        string repoRoot = XamlResourceValidationTests.FindRepoRoot();
        HashSet<string> en = ExtractKeys(Path.Join(repoRoot, "Coclico", "Resources", "Lang", "en.xaml"));

        Assert.All(checkIds, id =>
            Assert.Contains($"Diag_Check_{id.Replace('.', '_')}", en));
        Assert.Equal(checkIds.Length, CheckKeys.Length);
    }
}
