using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class CentralUnitVariablesTests
{
    [Fact]
    public void PublishThenGetReturnsTheValue()
    {
        var variables = new CentralUnitVariables();

        variables.Publish(CentralVariables.CpuPercent, 42.5);

        Assert.Equal(42.5, variables.Get<double>(CentralVariables.CpuPercent));
        Assert.True(variables.TryGet<double>(CentralVariables.CpuPercent, out double value));
        Assert.Equal(42.5, value);
    }

    [Fact]
    public void GetUnknownKeyThrows()
    {
        var variables = new CentralUnitVariables();

        Assert.Throws<KeyNotFoundException>(() => variables.Get<string>("Central.Tests.Missing"));
    }

    [Fact]
    public void TryGetWithUnexpectedTypeFails()
    {
        var variables = new CentralUnitVariables();

        variables.Publish(CentralVariables.RamInfo, "7,2 / 16 Go");

        Assert.False(variables.TryGet<double>(CentralVariables.RamInfo, out _));
    }

    [Fact]
    public void PublishingAnIdenticalValueIsIgnored()
    {
        var variables = new CentralUnitVariables();
        int changes = 0;
        variables.ValueChanged += (_, _) => changes++;

        variables.Publish(CentralVariables.CpuPercent, 10.0);
        variables.Publish(CentralVariables.CpuPercent, 10.0);
        variables.Publish(CentralVariables.CpuPercent, 20.0);

        Assert.Equal(2, changes);
        Assert.Equal(2, variables.PublishedCount);
    }

    [Fact]
    public void SubscribeDeliversTheCurrentValueThenFutureChanges()
    {
        var variables = new CentralUnitVariables();
        variables.Publish(CentralVariables.NetworkStats, "initial");
        var received = new List<string>();

        Action unsubscribe = variables.Subscribe(
            CentralVariables.NetworkStats,
            (_, value) => received.Add((string)value!));

        Assert.Single(received);

        variables.Publish(CentralVariables.NetworkStats, "updated");

        Assert.Equal(2, received.Count);
        Assert.Equal("updated", received[1]);

        unsubscribe();

        variables.Publish(CentralVariables.NetworkStats, "ignored");

        Assert.Equal(2, received.Count);
    }

    [Fact]
    public void SubscribeBeforeAnyPublishReceivesOnlyFutureValues()
    {
        var variables = new CentralUnitVariables();
        var received = new List<string>();

        _ = variables.Subscribe(CentralVariables.NetworkStats, (_, value) => received.Add((string)value!));

        Assert.Empty(received);

        variables.Publish(CentralVariables.NetworkStats, "first");

        Assert.Single(received);
        Assert.Equal("first", received[0]);
    }

    [Fact]
    public void UnsubscribeRemovesOnlyTheTargetedHandler()
    {
        var variables = new CentralUnitVariables();
        var first = new List<string>();
        var second = new List<string>();

        Action unsubscribeFirst = variables.Subscribe(
            CentralVariables.NetworkStats,
            (_, _) => first.Add("first"));
        _ = variables.Subscribe(
            CentralVariables.NetworkStats,
            (_, _) => second.Add("second"));

        variables.Publish(CentralVariables.NetworkStats, 1);
        unsubscribeFirst();
        variables.Publish(CentralVariables.NetworkStats, 2);

        Assert.Single(first);
        Assert.Equal(2, second.Count);
    }
}
