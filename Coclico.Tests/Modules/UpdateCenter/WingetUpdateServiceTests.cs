using Coclico.Modules.UpdateCenter.Models;
using Coclico.Modules.UpdateCenter.Services;
using Xunit;

namespace Coclico.Tests.Modules.UpdateCenter;

public sealed class WingetUpdateServiceTests
{
    [Theory]
    [InlineData("Firefox    Mozilla.Firefox    128.0    129.0.1    winget", "Mozilla.Firefox", "128.0", "129.0.1", UpdateItemStatus.Available)]
    [InlineData("Obsidian   Obsidian.Obsidian   Unknown  1.7.4       winget", "Obsidian.Obsidian", "Unknown", "1.7.4", UpdateItemStatus.UnknownVersion)]
    public void Parse_ExtractsExpectedItems(string row, string id, string current, string available, UpdateItemStatus status)
    {
        var items = WingetUpdateService.Parse(row);

        var item = Assert.Single(items);
        Assert.Equal(id, item.Id);
        Assert.Equal(current, item.CurrentVersion);
        Assert.Equal(available, item.AvailableVersion);
        Assert.Equal(status, item.Status);
        Assert.Equal(UpdateKind.Application, item.Kind);
        Assert.Equal(UpdateSource.Winget, item.Source);
    }

    [Fact]
    public void Parse_SkipsHeadersAndEmptyLines()
    {
        var output = """
            Name      Id      Version  Available  Source
            ----------------------------------------------------------
            """;

        Assert.Empty(WingetUpdateService.Parse(output));
        Assert.Empty(WingetUpdateService.Parse(string.Empty));
    }

    [Fact]
    public void Parse_DeduplicatesById()
    {
        var output = """
            Firefox    Mozilla.Firefox    128.0    129.0.1    winget
            Firefox    Mozilla.Firefox    128.0    129.0.1    winget
            """;

        Assert.Single(WingetUpdateService.Parse(output));
    }

    [Theory]
    [InlineData("Mozilla.Firefox")]
    [InlineData("Git.Git")]
    [InlineData("VideoLAN.VLC")]
    public void PackageIds_Accepted(string packageId)
    {
        Assert.True(WingetPackageIdValidator.IsValid(packageId));
    }

    [Theory]
    [InlineData("Mozilla.Firefox; calc")]
    [InlineData("bad id")]
    [InlineData("../escape")]
    [InlineData("")]
    public void PackageIds_Rejected(string packageId)
    {
        Assert.False(WingetPackageIdValidator.IsValid(packageId));
    }
}
