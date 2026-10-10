using Coclico.Modules.Power.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class AdaptivePowerRulesTests
{
    private static CpuLoadWindow BuildWindow(TimeSpan sampleInterval, TimeSpan duration, double usage, DateTime endUtc)
    {
        CpuLoadWindow window = new(TimeSpan.FromMinutes(5));
        DateTime cursor = endUtc - duration;
        while (cursor <= endUtc)
        {
            window.Add(usage, cursor);
            cursor += sampleInterval;
        }

        return window;
    }

    [Fact]
    public void CpuLoadWindow_AveragesOnlyRecentSamples()
    {
        CpuLoadWindow window = new(TimeSpan.FromMinutes(5));
        DateTime utc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        window.Add(80, utc.AddSeconds(-120));
        window.Add(20, utc.AddSeconds(-10));
        window.Add(40, utc);

        Assert.Equal(30, window.AverageLast(TimeSpan.FromSeconds(30), utc), 2);
        Assert.Equal((80 + 20 + 40) / 3.0, window.AverageLast(TimeSpan.FromSeconds(500), utc), 2);
    }

    [Fact]
    public void Evaluate_RespectsCooldown()
    {
        DateTime utc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        AdaptivePowerConfig config = new() { Cooldown = TimeSpan.FromSeconds(90) };
        CpuLoadWindow window = BuildWindow(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(60), 95, utc);

        (AdaptivePowerAction action, AdaptivePowerReason reason, _) = AdaptivePowerRules.Evaluate(
            window, config, CoclicoPowerMode.Economy, utc, lastSwitchUtc: utc.AddSeconds(-30));

        Assert.Equal(AdaptivePowerAction.None, action);
        Assert.Equal(AdaptivePowerReason.Cooldown, reason);
    }

    [Fact]
    public void Evaluate_SustainedHighLoad_SwitchesToPerformance()
    {
        DateTime utc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        AdaptivePowerConfig config = new();
        CpuLoadWindow window = BuildWindow(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(60), 90, utc);

        (AdaptivePowerAction action, AdaptivePowerReason reason, double load) = AdaptivePowerRules.Evaluate(
            window, config, CoclicoPowerMode.Economy, utc, lastSwitchUtc: utc.AddMinutes(-10));

        Assert.Equal(AdaptivePowerAction.SwitchToPerformance, action);
        Assert.Equal(AdaptivePowerReason.HighLoad, reason);
        Assert.InRange(load, 89.0, 91.0);
    }

    [Fact]
    public void Evaluate_SustainedLowLoad_SwitchesToEconomy()
    {
        DateTime utc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        AdaptivePowerConfig config = new();
        CpuLoadWindow window = BuildWindow(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(120), 5, utc);

        (AdaptivePowerAction action, AdaptivePowerReason reason, double load) = AdaptivePowerRules.Evaluate(
            window, config, CoclicoPowerMode.Performance, utc, lastSwitchUtc: utc.AddMinutes(-10));

        Assert.Equal(AdaptivePowerAction.SwitchToEconomy, action);
        Assert.Equal(AdaptivePowerReason.LowLoad, reason);
        Assert.InRange(load, 4.0, 6.0);
    }

    [Fact]
    public void Evaluate_ModerateLoad_DoesNothing()
    {
        DateTime utc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        AdaptivePowerConfig config = new();
        CpuLoadWindow window = BuildWindow(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(120), 40, utc);

        (AdaptivePowerAction action, AdaptivePowerReason reason, _) = AdaptivePowerRules.Evaluate(
            window, config, CoclicoPowerMode.Optimal, utc, lastSwitchUtc: utc.AddMinutes(-10));

        Assert.Equal(AdaptivePowerAction.None, action);
        Assert.Equal(AdaptivePowerReason.NormalLoad, reason);
    }

    [Fact]
    public void Evaluate_HighLoadOnPerformanceMode_DoesNotReapply()
    {
        DateTime utc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        AdaptivePowerConfig config = new();
        CpuLoadWindow window = BuildWindow(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(60), 95, utc);

        (AdaptivePowerAction action, AdaptivePowerReason _, _) = AdaptivePowerRules.Evaluate(
            window, config, CoclicoPowerMode.Performance, utc, lastSwitchUtc: utc.AddMinutes(-10));

        Assert.Equal(AdaptivePowerAction.None, action);
    }
}
