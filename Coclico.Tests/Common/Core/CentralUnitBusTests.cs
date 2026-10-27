using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class CentralUnitBusTests
{
    [Fact]
    public async Task QueryAsyncCachesTheResultForTheTtlDuration()
    {
        var bus = new CentralUnitBus();
        int factoryCalls = 0;

        string first = await bus.QueryAsync("Central.Tests.Gpu", ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult("value");
        }, TimeSpan.FromMinutes(1));

        string second = await bus.QueryAsync("Central.Tests.Gpu", ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult("other");
        }, TimeSpan.FromMinutes(1));

        Assert.Equal("value", first);
        Assert.Equal("value", second);
        Assert.Equal(1, factoryCalls);
        Assert.Equal(0, bus.DroppedRequests);
    }

    [Fact]
    public async Task ExpiredCacheTriggersANewFactoryCall()
    {
        var bus = new CentralUnitBus();
        int factoryCalls = 0;
        TimeSpan ttl = TimeSpan.FromMilliseconds(50);

        _ = await bus.QueryAsync("Central.Tests.Expired", ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult(1);
        }, ttl);

        await Task.Delay(150);

        _ = await bus.QueryAsync("Central.Tests.Expired", ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult(2);
        }, ttl);

        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task LowPriorityDoublesCacheLifetimeWhileCriticalHalvesIt()
    {
        var bus = new CentralUnitBus();
        int lowCalls = 0;
        int criticalCalls = 0;
        TimeSpan ttl = TimeSpan.FromMilliseconds(100);

        _ = await bus.QueryAsync("Central.Tests.Low", ct =>
        {
            Interlocked.Increment(ref lowCalls);
            return Task.FromResult(1);
        }, ttl, CentralRequestPriority.Low);

        await Task.Delay(150);

        _ = await bus.QueryAsync("Central.Tests.Low", ct =>
        {
            Interlocked.Increment(ref lowCalls);
            return Task.FromResult(1);
        }, ttl, CentralRequestPriority.Low);

        Assert.Equal(1, lowCalls);

        _ = await bus.QueryAsync("Central.Tests.Critical", ct =>
        {
            Interlocked.Increment(ref criticalCalls);
            return Task.FromResult(1);
        }, ttl, CentralRequestPriority.Critical);

        await Task.Delay(60);

        _ = await bus.QueryAsync("Central.Tests.Critical", ct =>
        {
            Interlocked.Increment(ref criticalCalls);
            return Task.FromResult(1);
        }, ttl, CentralRequestPriority.Critical);

        Assert.Equal(2, criticalCalls);
    }

    [Fact]
    public async Task ConcurrentQueriesShareASingleFactoryExecution()
    {
        var bus = new CentralUnitBus();
        int factoryCalls = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<string> first = bus.QueryAsync("Central.Tests.Shared", async ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            await release.Task;
            return "shared";
        }, TimeSpan.FromMinutes(1));

        Task<string> second = bus.QueryAsync("Central.Tests.Shared", async ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            await release.Task;
            return "shared";
        }, TimeSpan.FromMinutes(1));

        await Task.Delay(100);

        Assert.Equal(1, factoryCalls);

        release.SetResult();

        Assert.Equal("shared", await first);
        Assert.Equal("shared", await second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task InvalidateForcesANewFactoryCall()
    {
        var bus = new CentralUnitBus();
        int factoryCalls = 0;
        TimeSpan ttl = TimeSpan.FromMinutes(1);

        _ = await bus.QueryAsync("Central.Tests.Invalidated", ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult(1);
        }, ttl);

        bus.Invalidate("Central.Tests.Invalidated");

        _ = await bus.QueryAsync("Central.Tests.Invalidated", ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult(2);
        }, ttl);

        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task SendAsyncRoutesTypedCommandsToRegisteredHandlers()
    {
        var bus = new CentralUnitBus();
        string? routedCommand = null;

        bus.RegisterRoute<string>("Central.Tests.Route", (command, _) =>
        {
            routedCommand = command;
            return Task.CompletedTask;
        });

        await bus.SendAsync("Central.Tests.Route", "command");

        Assert.Equal("command", routedCommand);
        Assert.Equal(1, bus.RoutedRequests);
    }

    [Fact]
    public async Task SendAsyncOnUnknownRouteThrows()
    {
        var bus = new CentralUnitBus();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => bus.SendAsync("Central.Tests.Unknown", "command"));
    }
}
