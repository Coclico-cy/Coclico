using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class CentralCircuitBreakerTests
{
    [Fact]
    public void UnknownSubsystemIsClosedAndAllowed()
    {
        var breaker = new CentralCircuitBreaker();

        Assert.Equal(CentralCircuitState.Closed, breaker.GetState("Unknown"));
        Assert.True(breaker.IsAllowed("Unknown"));
    }

    [Fact]
    public void FailuresBelowThresholdKeepTheCircuitClosed()
    {
        var breaker = new CentralCircuitBreaker(failureThreshold: 3);

        breaker.RecordFailure("NetworkOptimizer");
        breaker.RecordFailure("NetworkOptimizer");

        Assert.Equal(CentralCircuitState.Closed, breaker.GetState("NetworkOptimizer"));
        Assert.True(breaker.IsAllowed("NetworkOptimizer"));
    }

    [Fact]
    public void SuccessResetsTheFailureStreak()
    {
        var breaker = new CentralCircuitBreaker(failureThreshold: 3);

        breaker.RecordFailure("AiChat");
        breaker.RecordFailure("AiChat");
        breaker.RecordSuccess("AiChat");
        breaker.RecordFailure("AiChat");
        breaker.RecordFailure("AiChat");

        Assert.Equal(CentralCircuitState.Closed, breaker.GetState("AiChat"));
    }

    [Fact]
    public void RepeatedFailuresOpenTheCircuitAndBlockCalls()
    {
        var breaker = new CentralCircuitBreaker(failureThreshold: 2);
        var transitions = new List<(string Subsystem, CentralCircuitState State)>();
        breaker.StateChanged += (subsystem, state) => transitions.Add((subsystem, state));

        breaker.RecordFailure("AiChat", new InvalidOperationException("boom"));
        breaker.RecordFailure("AiChat", new InvalidOperationException("boom"));

        Assert.Equal(CentralCircuitState.Open, breaker.GetState("AiChat"));
        Assert.False(breaker.IsAllowed("AiChat"));
        Assert.Contains(("AiChat", CentralCircuitState.Open), transitions);
    }

    [Fact]
    public async Task CooldownTurnsOpenIntoHalfOpenProbe()
    {
        var breaker = new CentralCircuitBreaker(failureThreshold: 1, openCooldown: TimeSpan.FromMilliseconds(80));

        breaker.RecordFailure("Installer");
        Assert.False(breaker.IsAllowed("Installer"));

        await Task.Delay(150);

        Assert.True(breaker.IsAllowed("Installer"));
        Assert.Equal(CentralCircuitState.HalfOpen, breaker.GetState("Installer"));
    }

    [Fact]
    public async Task HalfOpenFailureReopensAndSuccessCloses()
    {
        var breaker = new CentralCircuitBreaker(failureThreshold: 1, openCooldown: TimeSpan.FromMilliseconds(50));

        breaker.RecordFailure("Installer");
        await Task.Delay(100);

        Assert.True(breaker.IsAllowed("Installer"));

        breaker.RecordFailure("Installer", new TimeoutException("probe"));
        Assert.False(breaker.IsAllowed("Installer"));

        breaker.RecordSuccess("Installer");
        Assert.Equal(CentralCircuitState.Closed, breaker.GetState("Installer"));
        Assert.True(breaker.IsAllowed("Installer"));
    }

    [Fact]
    public void SnapshotExposesEveryTrackedSubsystem()
    {
        var breaker = new CentralCircuitBreaker(failureThreshold: 2);
        breaker.RecordFailure("NetworkOptimizer");
        breaker.RecordFailure("NetworkOptimizer");
        breaker.RecordFailure("DiskAnalyzer");

        List<(string Subsystem, CentralCircuitState State, int FailureStreak)> snapshot = breaker.GetSnapshot();

        Assert.Equal(2, snapshot.Count);
        Assert.Contains(snapshot, entry => entry.Subsystem == "NetworkOptimizer" && entry.State == CentralCircuitState.Open);
        Assert.Contains(snapshot, entry => entry.Subsystem == "DiskAnalyzer" && entry.FailureStreak == 1);
    }
}
