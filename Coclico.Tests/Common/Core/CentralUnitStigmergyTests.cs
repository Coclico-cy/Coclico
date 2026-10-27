using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public class CentralUnitStigmergyTests
{
    [Fact]
    public void RecordTransition_WithoutFrom_RecordsUsageOnly()
    {
        CentralUnitStigmergy sut = new();

        sut.RecordTransition(null, "Dashboard");
        sut.RecordTransition("dashboard", "   ");

        Assert.Empty(sut.GetTransitionSnapshot());
        List<CentralStigmergyModuleSnapshot> modules = sut.GetModuleSnapshot();
        CentralStigmergyModuleSnapshot snapshot = Assert.Single(modules);
        Assert.Equal("dashboard", snapshot.Module);
        Assert.Equal(1, snapshot.Uses);
        Assert.Equal("dashboard", sut.LastModule);
    }

    [Fact]
    public void RecordTransition_IgnoresSelfTransition()
    {
        CentralUnitStigmergy sut = new();

        sut.RecordTransition("Dashboard", "dashboard");

        Assert.Empty(sut.GetTransitionSnapshot());
        List<CentralStigmergyModuleSnapshot> modules = sut.GetModuleSnapshot();
        CentralStigmergyModuleSnapshot snapshot = Assert.Single(modules);
        Assert.Equal("dashboard", snapshot.Module);
        Assert.Equal(1, snapshot.Uses);
    }

    [Fact]
    public void PredictedNextModules_RankedByProbability()
    {
        CentralUnitStigmergy sut = new();

        sut.RecordTransition("dashboard", "ram");
        sut.RecordTransition("dashboard", "cleaning");
        sut.RecordTransition("dashboard", "cleaning");
        sut.RecordTransition("dashboard", "cleaning");

        List<(string Module, double Probability)> predicted = sut.GetPredictedNextModules("Dashboard", 5);

        Assert.Equal(2, predicted.Count);
        Assert.Equal("cleaning", predicted[0].Module);
        Assert.Equal("ram", predicted[1].Module);
        Assert.Equal(0.75, predicted[0].Probability, 2);
        Assert.Equal(0.25, predicted[1].Probability, 2);

        List<(string Module, double Probability)> topOne = sut.GetPredictedNextModules("dashboard", 1);
        Assert.Single(topOne);
        Assert.Equal("cleaning", topOne[0].Module);
    }

    [Fact]
    public void PredictedNextModules_EmptyWithoutHistory()
    {
        CentralUnitStigmergy sut = new();

        Assert.Empty(sut.GetPredictedNextModules("dashboard"));
        Assert.Empty(sut.GetPredictedNextModules("  "));

        sut.RecordModuleUsage("dashboard");
        Assert.Empty(sut.GetPredictedNextModules("dashboard"));
    }

    [Fact]
    public async Task TransitionDecay_ShiftsRankingOverTime()
    {
        CentralUnitStigmergy sut = new(transitionHalfLife: TimeSpan.FromMilliseconds(80));

        sut.RecordTransition("dashboard", "ram");
        sut.RecordTransition("dashboard", "ram");
        sut.RecordTransition("dashboard", "ram");

        await Task.Delay(250);

        sut.RecordTransition("dashboard", "cleaning");

        List<(string Module, double Probability)> predicted = sut.GetPredictedNextModules("dashboard", 5);
        Assert.NotEmpty(predicted);
        Assert.Equal("cleaning", predicted[0].Module);
        Assert.True(predicted.Count == 1 || predicted[1].Module == "ram");
    }

    [Fact]
    public void HourlyUsage_RanksModulesForCurrentHour()
    {
        CentralUnitStigmergy sut = new();
        int hour = DateTime.Now.Hour;

        sut.RecordModuleUsage("ram");
        sut.RecordModuleUsage("ram");
        sut.RecordModuleUsage("cleaning");

        if (DateTime.Now.Hour != hour)
        {
            sut.RecordModuleUsage("ram");
            sut.RecordModuleUsage("ram");
            sut.RecordModuleUsage("cleaning");
        }

        List<(string Module, double Score)> likely = sut.GetLikelyModulesAtHour(DateTime.Now.Hour, 5);
        Assert.Equal(2, likely.Count);
        Assert.Equal("ram", likely[0].Module);
        Assert.Equal("cleaning", likely[1].Module);

        Assert.Empty(sut.GetLikelyModulesAtHour(-1));
        Assert.Empty(sut.GetLikelyModulesAtHour(24));
    }

    [Fact]
    public void ModuleNames_NormalizedCaseInsensitive()
    {
        CentralUnitStigmergy sut = new();

        sut.RecordModuleUsage("Dashboard");
        sut.RecordModuleUsage("  DASHBOARD  ");

        List<CentralStigmergyModuleSnapshot> modules = sut.GetModuleSnapshot();
        CentralStigmergyModuleSnapshot snapshot = Assert.Single(modules);
        Assert.Equal("dashboard", snapshot.Module);
        Assert.Equal(2, snapshot.Uses);
    }

    [Fact]
    public void AdaptedTtl_UnknownKey_ReturnsBase()
    {
        CentralUnitStigmergy sut = new();
        TimeSpan baseTtl = TimeSpan.FromSeconds(60);

        Assert.Equal(baseTtl, sut.GetAdaptedTtl("unknown", baseTtl));
        Assert.Equal(baseTtl, sut.GetAdaptedTtl(null!, baseTtl));
        Assert.Equal(baseTtl, sut.GetAdaptedTtl("   ", baseTtl));
        Assert.Equal(TimeSpan.Zero, sut.GetAdaptedTtl("unknown", TimeSpan.Zero));
    }

    [Fact]
    public void AdaptedTtl_ExtendsAfterFreshStreak()
    {
        CentralUnitStigmergy sut = new();
        TimeSpan baseTtl = TimeSpan.FromSeconds(60);

        sut.RecordCacheOutcome("cpu.history", true);
        sut.RecordCacheOutcome("cpu.history", true);
        sut.RecordCacheOutcome("cpu.history", true);
        Assert.Equal(baseTtl, sut.GetAdaptedTtl("cpu.history", baseTtl));

        sut.RecordCacheOutcome("cpu.history", true);
        Assert.Equal(TimeSpan.FromSeconds(120), sut.GetAdaptedTtl("cpu.history", baseTtl));

        List<(string Key, double Factor, int FreshCount, int ExpiredCount)> snapshot = sut.GetTtlSnapshot();
        (string key, double factor, int freshCount, int expiredCount) = Assert.Single(snapshot);
        Assert.Equal("cpu.history", key);
        Assert.Equal(2.0, factor, 3);
        Assert.Equal(4, freshCount);
        Assert.Equal(0, expiredCount);
    }

    [Fact]
    public void AdaptedTtl_ShortensAfterExpiredStreak()
    {
        CentralUnitStigmergy sut = new();
        TimeSpan baseTtl = TimeSpan.FromSeconds(60);

        sut.RecordCacheOutcome("disk.map", false);
        Assert.Equal(baseTtl, sut.GetAdaptedTtl("disk.map", baseTtl));

        sut.RecordCacheOutcome("disk.map", false);
        Assert.Equal(TimeSpan.FromSeconds(30), sut.GetAdaptedTtl("disk.map", baseTtl));
    }

    [Fact]
    public void AdaptedTtl_StreaksResetOnOppositeOutcome()
    {
        CentralUnitStigmergy sut = new();
        TimeSpan baseTtl = TimeSpan.FromSeconds(60);

        sut.RecordCacheOutcome("app.list", true);
        sut.RecordCacheOutcome("app.list", true);
        sut.RecordCacheOutcome("app.list", true);
        sut.RecordCacheOutcome("app.list", false);
        sut.RecordCacheOutcome("app.list", true);

        Assert.Equal(baseTtl, sut.GetAdaptedTtl("app.list", baseTtl));
    }

    [Fact]
    public void AdaptedTtl_FactorStaysWithinBounds()
    {
        CentralUnitStigmergy sut = new();
        TimeSpan baseTtl = TimeSpan.FromSeconds(60);

        for (int i = 0; i < 10; i++)
        {
            sut.RecordCacheOutcome("cold", false);
        }
        Assert.Equal(TimeSpan.FromSeconds(15), sut.GetAdaptedTtl("cold", baseTtl));

        for (int i = 0; i < 20; i++)
        {
            sut.RecordCacheOutcome("hot", true);
        }
        Assert.Equal(TimeSpan.FromSeconds(240), sut.GetAdaptedTtl("hot", baseTtl));
    }

    [Fact]
    public void TtlCapacity_StaysBounded()
    {
        CentralUnitStigmergy sut = new();

        for (int i = 0; i < 200; i++)
        {
            sut.RecordCacheOutcome($"key.{i}", true);
        }

        Assert.True(sut.GetTtlSnapshot().Count <= 128);
    }

    [Fact]
    public void Clear_ResetsEverything()
    {
        CentralUnitStigmergy sut = new();

        sut.RecordTransition("dashboard", "ram");
        sut.RecordCacheOutcome("cpu.history", true);

        sut.Clear();

        Assert.Null(sut.LastModule);
        Assert.Empty(sut.GetModuleSnapshot());
        Assert.Empty(sut.GetTransitionSnapshot());
        Assert.Empty(sut.GetTtlSnapshot());
        Assert.Empty(sut.GetLikelyModulesAtHour(DateTime.Now.Hour));
        Assert.Equal(TimeSpan.FromSeconds(60), sut.GetAdaptedTtl("cpu.history", TimeSpan.FromSeconds(60)));
    }
}
