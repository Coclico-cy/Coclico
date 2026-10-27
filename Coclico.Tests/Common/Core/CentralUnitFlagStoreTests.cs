using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class CentralUnitFlagStoreTests : IDisposable
{
    private readonly CentralUnitFlagStore _store = new();

    public void Dispose()
    {
        _store.Dispose();
    }

    [Fact]
    public async Task WaitAsyncCompletesImmediatelyWhenFlagIsAlreadySet()
    {
        _store.Set(CentralFlags.NetworkStatsDue);

        Assert.True(await _store.WaitAsync(CentralFlags.NetworkStatsDue));
    }

    [Fact]
    public async Task WaitAsyncCompletesWhenFlagIsSetLater()
    {
        Task<bool> wait = _store.WaitAsync(CentralFlags.GuardTickDue);

        Assert.False(wait.IsCompleted);

        _store.Set(CentralFlags.GuardTickDue);

        Assert.True(await wait);
    }

    [Fact]
    public async Task WaitAsyncReturnsFalseWhenFlagIsClearedWhileWaiting()
    {
        Task<bool> wait = _store.WaitAsync(CentralFlags.GuardTickDue);

        _store.Clear(CentralFlags.GuardTickDue);

        Assert.False(await wait);
    }

    [Fact]
    public async Task WaitAsyncHonorsCancellation()
    {
        using var cts = new CancellationTokenSource();

        Task<bool> wait = _store.WaitAsync(CentralFlags.StartupCompleted, cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }

    [Fact]
    public async Task PendingFlagIsConsumedByASingleWaiter()
    {
        _store.Set(CentralFlags.RamDaemonCycleDue);
        Assert.True(await _store.WaitAsync(CentralFlags.RamDaemonCycleDue));

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _store.WaitAsync(CentralFlags.RamDaemonCycleDue, timeoutCts.Token));
    }

    [Fact]
    public async Task SetWakesEveryWaiterOfTheSameFlag()
    {
        Task<bool> first = _store.WaitAsync(CentralFlags.DashboardRefreshDue);
        Task<bool> second = _store.WaitAsync(CentralFlags.DashboardRefreshDue);

        _store.Set(CentralFlags.DashboardRefreshDue);

        Assert.True(await first);
        Assert.True(await second);
    }

    [Fact]
    public async Task ClearRemovesAPendingFlagBeforeAnyWaiterConsumesIt()
    {
        _store.Set(CentralFlags.PressureSample);
        _store.Clear(CentralFlags.PressureSample);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _store.WaitAsync(CentralFlags.PressureSample, timeoutCts.Token));
    }

    [Fact]
    public void SetIncrementsWakeups()
    {
        long wakeupsBefore = _store.Wakeups;

        _store.Set(CentralFlags.NetworkStatsDue);
        _store.Set(CentralFlags.GuardTickDue);

        Assert.Equal(wakeupsBefore + 2, _store.Wakeups);
    }

    [Fact]
    public void ScheduleAndCancelTrackScheduledCount()
    {
        _store.Schedule(CentralFlags.AiModelUnload, DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(1, _store.ScheduledCount);

        _store.CancelSchedule(CentralFlags.AiModelUnload);

        Assert.Equal(0, _store.ScheduledCount);
    }

    [Fact]
    public async Task SchedulerFiresDueSchedules()
    {
        _store.StartScheduler();
        _store.Schedule(CentralFlags.PressureSample, DateTime.UtcNow.AddMilliseconds(50));

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Assert.True(await _store.WaitAsync(CentralFlags.PressureSample, timeoutCts.Token));
        Assert.Equal(0, _store.ScheduledCount);
    }

    [Fact]
    public async Task PostponeNonCriticalDelaysOnlyNonCriticalSchedules()
    {
        _store.SetCriticalFlags(CentralFlags.StartupCompleted);
        _store.StartScheduler();
        _store.Schedule(CentralFlags.StartupCompleted, DateTime.UtcNow.AddMilliseconds(50));
        _store.Schedule(CentralFlags.NetworkStatsDue, DateTime.UtcNow.AddMilliseconds(50));
        _store.PostponeNonCritical(TimeSpan.FromMinutes(30));

        using var criticalCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.True(await _store.WaitAsync(CentralFlags.StartupCompleted, criticalCts.Token));

        using var postponedCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _store.WaitAsync(CentralFlags.NetworkStatsDue, postponedCts.Token));
    }
}
