using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class CentralUnitServiceTests
{
    [Fact]
    public void CurrentIsASingletonExposingEveryOrgan()
    {
        Assert.NotNull(CentralUnitService.Current);
        Assert.Same(CentralUnitService.Current, CentralUnitService.Current);
        Assert.NotNull(CentralUnitService.Current.Flags);
        Assert.NotNull(CentralUnitService.Current.Activity);
        Assert.NotNull(CentralUnitService.Current.Variables);
        Assert.NotNull(CentralUnitService.Current.Bus);
    }

    [Fact]
    public void DefaultAiUnloadDelayIsThirtySeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), CentralUnitService.DefaultAiUnloadDelay);
    }

    [Fact]
    public void GetStatsReportsZeroBoostWhenIdle()
    {
        CentralUnitService.Current.SetAiInference(false);

        CentralUnitStats stats = CentralUnitService.Current.GetStats();

        Assert.Equal(0, stats.BoostDepth);
        Assert.False(stats.IsRunning);
        Assert.False(stats.ExternalPressureActive);
    }

    [Fact]
    public void SetAiInferenceTogglesBoostAndTheModelUnloadSchedule()
    {
        CentralUnitService.Current.SetAiInference(false);

        CentralUnitService.Current.SetAiInference(true);

        CentralUnitStats boosted = CentralUnitService.Current.GetStats();
        Assert.True(CentralUnitService.Current.Activity.IsAiInferenceActive);
        Assert.Equal(1, boosted.BoostDepth);
        Assert.Equal(0, boosted.ScheduledFlags);

        CentralUnitService.Current.SetAiInference(false);

        CentralUnitStats eased = CentralUnitService.Current.GetStats();
        Assert.False(CentralUnitService.Current.Activity.IsAiInferenceActive);
        Assert.Equal(0, eased.BoostDepth);
        Assert.Equal(1, eased.ScheduledFlags);
    }

    [Fact]
    public void NotifyAiActivitySchedulesUnloadOnlyWhenInferenceIsIdle()
    {
        CentralUnitService.Current.SetAiInference(false);
        CentralUnitService.Current.CancelAiUnload();
        Assert.Equal(0, CentralUnitService.Current.GetStats().ScheduledFlags);

        CentralUnitService.Current.NotifyAiActivity();
        Assert.Equal(1, CentralUnitService.Current.GetStats().ScheduledFlags);

        CentralUnitService.Current.SetAiInference(true);
        CentralUnitService.Current.CancelAiUnload();
        CentralUnitService.Current.NotifyAiActivity();
        Assert.Equal(0, CentralUnitService.Current.GetStats().ScheduledFlags);

        CentralUnitService.Current.SetAiInference(false);
    }

    [Fact]
    public void MarkStartupCompletedLeavesStartingUp()
    {
        CentralUnitService.Current.SetAiInference(false);
        CentralUnitService.Current.SetWindowVisible(false);
        CentralUnitService.Current.SetTrayMinimized(false);

        CentralUnitService.Current.MarkStartupCompleted();

        Assert.Equal(CentralActivityLevel.Background, CentralUnitService.Current.Activity.Level);
    }

    [Fact]
    public void WindowAndTrayStateDriveTheActivityLevel()
    {
        CentralUnitService.Current.SetAiInference(false);
        CentralUnitService.Current.MarkStartupCompleted();

        CentralUnitService.Current.SetWindowVisible(true);
        Assert.Equal(CentralActivityLevel.Active, CentralUnitService.Current.Activity.Level);

        CentralUnitService.Current.SetWindowVisible(false);
        CentralUnitService.Current.SetTrayMinimized(true);
        Assert.Equal(CentralActivityLevel.Idle, CentralUnitService.Current.Activity.Level);

        CentralUnitService.Current.SetTrayMinimized(false);
    }

    [Fact]
    public void GetStatsAggregatesFlagAndVariableCounters()
    {
        CentralUnitService.Current.SetAiInference(false);
        long wakeupsBefore = CentralUnitService.Current.GetStats().FlagWakeups;
        long publishedBefore = CentralUnitService.Current.GetStats().PublishedValues;

        CentralUnitService.Current.Flags.Set(CentralFlags.NetworkStatsDue);
        CentralUnitService.Current.Variables.Publish("Central.Tests.Aggregate", 1);

        CentralUnitStats stats = CentralUnitService.Current.GetStats();
        Assert.Equal(wakeupsBefore + 1, stats.FlagWakeups);
        Assert.Equal(publishedBefore + 1, stats.PublishedValues);
    }

    [Fact]
    public void ShutdownWithoutInitializeIsSafeAndIdempotent()
    {
        CentralUnitService.Current.Shutdown();
        CentralUnitService.Current.Shutdown();

        Assert.False(CentralUnitService.Current.GetStats().IsRunning);
    }
}
