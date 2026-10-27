using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class CentralUnitWatchdogTests : IDisposable
{
    private readonly CentralUnitFlagStore _store = new();
    private readonly CentralUnitActivity _activity = new();
    private readonly CentralUnitWatchdog _watchdog;

    public CentralUnitWatchdogTests()
    {
        _watchdog = new CentralUnitWatchdog(_store, _activity);
    }

    public void Dispose()
    {
        _watchdog.Dispose();
        _store.Dispose();
    }

    [Fact]
    public void BeatTracksFreshSubsystems()
    {
        _watchdog.Register("NetworkMonitor", TimeSpan.FromSeconds(5));
        _watchdog.Beat("NetworkMonitor");
        _watchdog.Beat("NetworkMonitor");
        _watchdog.Beat("Unknown");

        List<CentralWatchdogSnapshot> snapshots = _watchdog.GetSnapshot();

        Assert.Single(snapshots);
        Assert.Equal("NetworkMonitor", snapshots[0].Subsystem);
        Assert.Equal(2, snapshots[0].BeatCount);
        Assert.NotNull(snapshots[0].LastBeatUtc);
        Assert.False(snapshots[0].IsStale);
    }

    [Fact]
    public void CheckAllIgnoresFreshSubsystems()
    {
        _watchdog.Register("ResourceGuard", TimeSpan.FromSeconds(60));

        Assert.Equal(0, _watchdog.CheckAll());
        Assert.False(_watchdog.GetSnapshot().Single().IsStale);
    }

    [Fact]
    public async Task CheckAllReportsAndRevivesSilentSubsystems()
    {
        int revives = 0;
        _watchdog.Register("SmartMemoryDaemon", TimeSpan.FromMilliseconds(50), () => Interlocked.Increment(ref revives));

        await Task.Delay(120);

        Assert.Equal(1, _watchdog.CheckAll());
        Assert.Equal(1, Volatile.Read(ref revives));

        CentralWatchdogSnapshot snapshot = _watchdog.GetSnapshot().Single();
        Assert.True(snapshot.IsStale);
        Assert.Equal(1, snapshot.StaleCount);

        _watchdog.Beat("SmartMemoryDaemon");

        Assert.Equal(0, _watchdog.CheckAll());
        Assert.False(_watchdog.GetSnapshot().Single().IsStale);
    }

    [Fact]
    public async Task CheckAllSwallowsReviveFailures()
    {
        _watchdog.Register("Broken", TimeSpan.FromMilliseconds(50), () => throw new InvalidOperationException("revive failed"));

        await Task.Delay(120);

        Assert.Equal(1, _watchdog.CheckAll());
    }

    [Fact]
    public void RegisterOverwritesAndIgnoresInvalidEntries()
    {
        _watchdog.Register("Dashboard", TimeSpan.FromSeconds(5), () => { });
        _watchdog.Beat("Dashboard");
        _watchdog.Register("Dashboard", TimeSpan.FromSeconds(60));

        _watchdog.Register("   ", TimeSpan.FromSeconds(5));
        _watchdog.Register("Invalid", TimeSpan.Zero);

        List<CentralWatchdogSnapshot> snapshots = _watchdog.GetSnapshot();

        Assert.Single(snapshots);
        Assert.Equal(TimeSpan.FromSeconds(60), snapshots[0].MaxSilence);
        Assert.Equal(0, snapshots[0].BeatCount);
    }

    [Fact]
    public void UnregisterRemovesTheSubsystem()
    {
        _watchdog.Register("Dashboard", TimeSpan.FromSeconds(5));
        _watchdog.Unregister("Dashboard");

        Assert.Empty(_watchdog.GetSnapshot());
        Assert.Equal(0, _watchdog.CheckAll());
    }

    [Fact]
    public async Task StartDrivesPeriodicChecksUntilStopped()
    {
        using (var periodicWatchdog = new CentralUnitWatchdog(_store, _activity, TimeSpan.FromMilliseconds(60)))
        {
            int revives = 0;
            periodicWatchdog.Register("Stalled", TimeSpan.FromMilliseconds(30), () => Interlocked.Increment(ref revives));

            _store.StartScheduler();
            periodicWatchdog.Start();
            periodicWatchdog.Start();

            try
            {
                DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (Volatile.Read(ref revives) == 0 && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(50);
                }
            }
            finally
            {
                periodicWatchdog.Stop();
            }

            Assert.True(revives >= 1);
        }
    }

    [Fact]
    public void StopIsIdempotentAndKeepsManualChecksUsable()
    {
        _watchdog.Register("Manual", TimeSpan.FromSeconds(30));
        _watchdog.Beat("Manual");

        _watchdog.Stop();
        _watchdog.Stop();

        Assert.Equal(0, _watchdog.CheckAll());
        Assert.Single(_watchdog.GetSnapshot());
    }
}
