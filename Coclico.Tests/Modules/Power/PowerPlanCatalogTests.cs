using Coclico.Modules.Power.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class PowerPlanCatalogTests
{
    [Fact]
    public void PlanNames_AreTheThreeCoclicoPlans()
    {
        Assert.Equal("Coclico economie", PowerPlanCatalog.GetPlanName(CoclicoPowerMode.Economy));
        Assert.Equal("Coclico optimal", PowerPlanCatalog.GetPlanName(CoclicoPowerMode.Optimal));
        Assert.Equal("Coclico performance", PowerPlanCatalog.GetPlanName(CoclicoPowerMode.Performance));
    }

    [Fact]
    public void TryGetModeFromPlanName_RoundTripsAndIgnoresCase()
    {
        Assert.Equal(CoclicoPowerMode.Economy, PowerPlanCatalog.TryGetModeFromPlanName("Coclico economie"));
        Assert.Equal(CoclicoPowerMode.Optimal, PowerPlanCatalog.TryGetModeFromPlanName(" coclico OPTIMAL "));
        Assert.Equal(CoclicoPowerMode.Performance, PowerPlanCatalog.TryGetModeFromPlanName("Coclico Performance"));
        Assert.Null(PowerPlanCatalog.TryGetModeFromPlanName("Équilibré"));
        Assert.Null(PowerPlanCatalog.TryGetModeFromPlanName(null));
        Assert.Null(PowerPlanCatalog.TryGetModeFromPlanName(""));
    }

    [Fact]
    public void BuildSetIndexArguments_BuildsPowercfgCommands()
    {
        PowerSettingOverride setting = new(
            PowerPlanCatalog.SubProcessor,
            PowerPlanCatalog.PerfEpp,
            "PERFEPP",
            AcValue: 40,
            DcValue: 60);

        IReadOnlyList<string> ac = PowerPlanCatalog.BuildSetIndexArguments("11111111-2222-3333-4444-555555555555", setting, ac: true);
        Assert.Equal(
        [
            "/setacvalueindex",
            "11111111-2222-3333-4444-555555555555",
            PowerPlanCatalog.SubProcessor,
            PowerPlanCatalog.PerfEpp,
            "40"
        ], ac);

        IReadOnlyList<string> dc = PowerPlanCatalog.BuildSetIndexArguments("11111111-2222-3333-4444-555555555555", setting, ac: false);
        Assert.Equal("/setdcvalueindex", dc[0]);
        Assert.Equal("60", dc[4]);
    }

    [Fact]
    public void Settings_CoverTheThreeModesWithDistinctValues()
    {
        foreach (CoclicoPowerMode mode in Enum.GetValues<CoclicoPowerMode>())
        {
            IReadOnlyList<PowerSettingOverride> settings = PowerPlanCatalog.GetSettings(mode);

            Assert.True(settings.Count >= 15, $"Le mode {mode} devrait couvrir les sous-groupes clés.");
            Assert.Contains(settings, s => s.SettingGuid == PowerPlanCatalog.ProcThrottleMin);
            Assert.Contains(settings, s => s.SettingGuid == PowerPlanCatalog.PerfBoostMode);
            Assert.Contains(settings, s => s.SettingGuid == PowerPlanCatalog.SysCoolPolicy);
            Assert.All(settings, s => Assert.True(s.AcValue <= 3600, $"AC hors bornes : {s.Alias}={s.AcValue}"));
            Assert.All(settings, s => Assert.True(s.DcValue <= 3600, $"DC hors bornes : {s.Alias}={s.DcValue}"));
        }

        IReadOnlyList<PowerSettingOverride> economy = PowerPlanCatalog.GetSettings(CoclicoPowerMode.Economy);
        IReadOnlyList<PowerSettingOverride> optimal = PowerPlanCatalog.GetSettings(CoclicoPowerMode.Optimal);
        IReadOnlyList<PowerSettingOverride> performance = PowerPlanCatalog.GetSettings(CoclicoPowerMode.Performance);

        Assert.Equal(100u, economy.First(s => s.SettingGuid == PowerPlanCatalog.PerfEpp).AcValue);
        Assert.Equal(0u, performance.First(s => s.SettingGuid == PowerPlanCatalog.PerfEpp).AcValue);
        Assert.True(optimal.First(s => s.SettingGuid == PowerPlanCatalog.PerfEpp).AcValue
            < economy.First(s => s.SettingGuid == PowerPlanCatalog.PerfEpp).AcValue);
        Assert.Equal(100u, performance.First(s => s.SettingGuid == PowerPlanCatalog.ProcThrottleMin).AcValue);
    }

    [Fact]
    public void BaseSchemes_MatchWindowsBuiltins()
    {
        Assert.Equal(PowerPlanCatalog.BasePowerSaverGuid, PowerPlanCatalog.GetBaseSchemeGuid(CoclicoPowerMode.Economy));
        Assert.Equal(PowerPlanCatalog.BaseBalancedGuid, PowerPlanCatalog.GetBaseSchemeGuid(CoclicoPowerMode.Optimal));
        Assert.Equal(PowerPlanCatalog.BaseHighPerformanceGuid, PowerPlanCatalog.GetBaseSchemeGuid(CoclicoPowerMode.Performance));
    }
}
