using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class LaunchModesTests
{
    [Theory]
    [InlineData("Normal")]
    [InlineData("Minimized")]
    [InlineData("Tray")]
    [InlineData("Maximized")]
    [InlineData("tray")]
    [InlineData("MAXIMIZED")]
    public void IsValid_AcceptsDefinedModesCaseInsensitive(string value)
    {
        Assert.True(LaunchModes.IsValid(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("tr a y")]
    public void IsValid_RejectsUnknownValues(string? value)
    {
        Assert.False(LaunchModes.IsValid(value));
    }

    [Theory]
    [InlineData("Normal", LaunchMode.Normal)]
    [InlineData("minimized", LaunchMode.Minimized)]
    [InlineData("Tray", LaunchMode.Tray)]
    [InlineData("MAXIMIZED", LaunchMode.Maximized)]
    public void Parse_ReturnsDefinedMode(string value, LaunchMode expected)
    {
        Assert.Equal(expected, LaunchModes.Parse(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown-mode")]
    [InlineData("42")]
    public void Parse_FallsBackToNormal(string? value)
    {
        Assert.Equal(LaunchMode.Normal, LaunchModes.Parse(value));
    }

    [Fact]
    public void Parse_ReturnedValuesAreAlwaysDefined()
    {
        foreach (string? candidate in new[] { null, "", "tray", "999", "normal" })
        {
            Assert.True(Enum.IsDefined(LaunchModes.Parse(candidate)));
        }
    }
}
