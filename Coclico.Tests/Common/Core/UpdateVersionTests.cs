using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class UpdateVersionTests
{
    [Theory]
    [InlineData("v2.0.1", "2.0.0", true)]
    [InlineData("v2.0.0", "2.0.0", false)]
    [InlineData("v2.1.0", "2.0.9", true)]
    [InlineData("v3.0", "2.9.9", true)]
    [InlineData("v2.0.10", "2.0.9", true)]
    [InlineData("v2.0.9", "2.0.10", false)]
    [InlineData("2.0.0", "2.0.0-beta", true)]
    [InlineData("2.0.0-beta", "2.0.0", false)]
    public void IsNewerVersion_ComparesNumericallyAndHandlesPrereleases(string latestTag, string currentVersion, bool expected)
    {
        Assert.Equal(expected, UpdateManager.IsNewerVersion(latestTag, currentVersion));
    }

    [Theory]
    [InlineData("2.0.1", "v2.0.1", true)]
    [InlineData("v2.0.1", "2.0.1", true)]
    [InlineData("2.0.1+d4f8c2", "v2.0.1", true)]
    [InlineData("2.0.2", "v2.0.1", false)]
    [InlineData("2.0.1", "v2.0.1-Full-CUDA", false)]
    [InlineData(null, "v2.0.1", false)]
    [InlineData("", "v2.0.1", false)]
    [InlineData("2.0.1", "", false)]
    public void InstallerVersionMatches_NormalizesPrefixesAndBuildSuffixes(string? fileVersion, string expectedVersion, bool expected)
    {
        Assert.Equal(expected, UpdateManager.InstallerVersionMatches(fileVersion, expectedVersion));
    }

    [Fact]
    public void ReadInstallerVersion_ReadsAssemblyVersionWithoutRunningIt()
    {
        string ownAssembly = typeof(UpdateVersionTests).Assembly.Location;

        string? version = UpdateManager.ReadInstallerVersion(ownAssembly);

        Assert.NotNull(version);
        Assert.Matches(@"^\d+(\.\d+)*", version);
    }

    [Fact]
    public void ReadInstallerVersion_MissingFile_ReturnsNull()
    {
        Assert.Null(UpdateManager.ReadInstallerVersion(Path.Combine(Path.GetTempPath(), "coclico-absent-" + Guid.NewGuid().ToString("N") + ".exe")));
    }

    [Theory]
    [InlineData("v2.0.1", "v2.0.1", true)]
    [InlineData("2.0.1", "v2.0.1", true)]
    [InlineData("v2.0.1", "2.0.1", true)]
    [InlineData("v2.0.1", "v2.0.2", false)]
    [InlineData("v2.0.1", "", false)]
    [InlineData("v2.0.1", null, false)]
    [InlineData(null, "v2.0.1", false)]
    public void IsSkipped_MatchesOnlyTheExactIgnoredVersion(string? tagName, string? skippedVersion, bool expected)
    {
        Assert.Equal(expected, UpdateManager.IsSkipped(tagName, skippedVersion));
    }

    [Fact]
    public void PickSetupAsset_PrefersStandardSetupOverCuda()
    {
        GitHubRelease release = new()
        {
            TagName = "v2.0.1",
            Assets =
            [
                new GitHubAsset { Name = "Coclico-v2.0.1-win-x64.zip" },
                new GitHubAsset { Name = "Coclico-2.0.1-Full-CUDA.exe" },
                new GitHubAsset { Name = "SHA256SUMS.txt" },
                new GitHubAsset { Name = "Coclico-2.0.1.exe" }
            ]
        };

        GitHubAsset? asset = UpdateManager.PickSetupAsset(release);

        Assert.NotNull(asset);
        Assert.Equal("Coclico-2.0.1.exe", asset.Name);
    }

    [Fact]
    public void PickSetupAsset_ReturnsNullWhenNoSetup()
    {
        GitHubRelease release = new()
        {
            TagName = "v2.0.1",
            Assets = [new GitHubAsset { Name = "Coclico-v2.0.1-win-x64.zip" }]
        };

        Assert.Null(UpdateManager.PickSetupAsset(release));
    }

    [Fact]
    public void PickSetupAsset_IgnoresCudaOnlyRelease()
    {
        GitHubRelease release = new()
        {
            TagName = "v2.0.1",
            Assets = [new GitHubAsset { Name = "Coclico-2.0.1-Full-CUDA.exe" }]
        };

        Assert.Null(UpdateManager.PickSetupAsset(release));
    }
}