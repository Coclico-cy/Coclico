using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class CentralErrorTriageTests
{
    [Fact]
    public void NullExceptionIsIgnored()
    {
        var triage = new CentralErrorTriage();

        triage.Record(null, "Central.Tests");

        Assert.Equal(0, triage.TotalRecords);
        Assert.Equal(0, triage.DistinctCount);
    }

    [Fact]
    public void IdenticalErrorsAreCountedAsASingleEntry()
    {
        var triage = new CentralErrorTriage();
        var exception = new InvalidOperationException("boom");

        triage.Record(exception, "AiChatService.Infer");
        triage.Record(new InvalidOperationException("boom"), "AiChatService.Infer");
        triage.Record(new InvalidOperationException("boom"), "AiChatService.Infer");

        Assert.Equal(3, triage.TotalRecords);
        Assert.Equal(1, triage.DistinctCount);

        CentralErrorEntry top = triage.GetTop(10).Single();
        Assert.Equal("AiChatService.Infer", top.Context);
        Assert.Equal(3, top.Count);
    }

    [Fact]
    public void DifferentContextOrMessageKeepsSeparateEntries()
    {
        var triage = new CentralErrorTriage();

        triage.Record(new InvalidOperationException("boom"), "Context.A");
        triage.Record(new InvalidOperationException("boom"), "Context.B");
        triage.Record(new InvalidOperationException("other"), "Context.A");
        triage.Record(new ArgumentException("boom"), "Context.A");

        Assert.Equal(4, triage.DistinctCount);
    }

    [Fact]
    public void GetTopOrdersByCountAndClampsToMax()
    {
        var triage = new CentralErrorTriage();

        for (int i = 0; i < 3; i++)
        {
            triage.Record(new InvalidOperationException("frequent"), "Context.A");
        }

        triage.Record(new InvalidOperationException("rare"), "Context.B");

        Assert.Equal("frequent", triage.GetTop(10)[0].Message);
        Assert.Single(triage.GetTop(1));
        Assert.Equal("frequent", triage.GetTop(1)[0].Message);
    }

    [Fact]
    public async Task FirstAndLastSeenAreTracked()
    {
        var triage = new CentralErrorTriage();

        triage.Record(new InvalidOperationException("boom"), "Context.A");
        await Task.Delay(30);
        triage.Record(new InvalidOperationException("boom"), "Context.A");

        CentralErrorEntry entry = triage.GetTop(10).Single();

        Assert.True(entry.LastSeenUtc >= entry.FirstSeenUtc);
        Assert.True(entry.FirstSeenUtc > DateTime.MinValue);
    }

    [Fact]
    public void ClearResetsAllCounters()
    {
        var triage = new CentralErrorTriage();
        triage.Record(new InvalidOperationException("boom"), "Context.A");

        triage.Clear();

        Assert.Equal(0, triage.TotalRecords);
        Assert.Equal(0, triage.DistinctCount);
        Assert.Empty(triage.GetTop(10));
    }

    [Fact]
    public void CapacityTrimsTheLeastRecentlySeenEntries()
    {
        var triage = new CentralErrorTriage();

        for (int i = 0; i < 260; i++)
        {
            triage.Record(new InvalidOperationException($"error-{i}"), "Context.A");
        }

        Assert.Equal(256, triage.DistinctCount);

        List<CentralErrorEntry> top = triage.GetTop(300);
        Assert.DoesNotContain(top, entry => entry.Message == "error-0");
        Assert.Contains(top, entry => entry.Message == "error-259");
    }
}
