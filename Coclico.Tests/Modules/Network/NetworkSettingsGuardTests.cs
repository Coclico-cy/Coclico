using Coclico.Models.Network;
using Coclico.Services.Network;
using Xunit;

namespace Coclico.Tests;

public sealed class NetworkSettingsGuardTests
{
    [Fact]
    public void DefaultTcpSettingsAreWhitelisted()
    {
        Assert.True(NetworkSettingsGuard.IsValidTcpSettings(new TcpGlobalSettings()));
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("disabled")]
    [InlineData("restricted")]
    [InlineData("highlyrestricted")]
    [InlineData("experimental")]
    public void AllDocumentedAutoTuningLevelsAreAccepted(string level)
    {
        var settings = new TcpGlobalSettings { AutoTuningLevel = level };
        Assert.True(NetworkSettingsGuard.IsValidTcpSettings(settings));
    }

    [Theory]
    [InlineData("normal & calc.exe")]
    [InlineData("normal; calc.exe")]
    [InlineData("normal'")]
    [InlineData("normal|whoami")]
    [InlineData("")]
    [InlineData("ultra")]
    [InlineData("none")]
    public void UnknownOrMaliciousAutoTuningLevelIsRejected(string level)
    {
        var settings = new TcpGlobalSettings { AutoTuningLevel = level };
        Assert.False(NetworkSettingsGuard.IsValidTcpSettings(settings));
    }

    [Theory]
    [InlineData("enabled")]
    [InlineData("disabled")]
    public void OnOffStatesAreAccepted(string state)
    {
        var settings = new TcpGlobalSettings { Rss = state, Rsc = state, Timestamps = state, EcnCapability = state };
        Assert.True(NetworkSettingsGuard.IsValidTcpSettings(settings));
    }

    [Theory]
    [InlineData("enabled; whoami")]
    [InlineData("disabled && calc")]
    [InlineData("both")]
    [InlineData("ENABLED OR 1=1")]
    public void UnknownRssStateIsRejected(string state)
    {
        var settings = new TcpGlobalSettings { Rss = state };
        Assert.False(NetworkSettingsGuard.IsValidTcpSettings(settings));
    }

    [Theory]
    [InlineData("ctcp")]
    [InlineData("newreno")]
    [InlineData("none")]
    public void KnownCongestionProvidersAreAccepted(string provider)
    {
        var settings = new TcpGlobalSettings { CongestionProvider = provider };
        Assert.True(NetworkSettingsGuard.IsValidTcpSettings(settings));
    }

    [Theory]
    [InlineData("ctcp; calc.exe")]
    [InlineData("ctcp'")]
    [InlineData("default")]
    [InlineData("bbr")]
    public void UnknownCongestionProviderIsRejected(string provider)
    {
        var settings = new TcpGlobalSettings { CongestionProvider = provider };
        Assert.False(NetworkSettingsGuard.IsValidTcpSettings(settings));
    }

    [Fact]
    public void NullTcpSettingsAreRejected()
    {
        Assert.False(NetworkSettingsGuard.IsValidTcpSettings(null!));
    }

    [Theory]
    [InlineData("Ethernet")]
    [InlineData("Connexion réseau locale 2")]
    [InlineData("Wi-Fi 6E (2.4 Gbit/s)")]
    [InlineData("{4D36E972-E325-11CE-BFC1-08002BE10318}")]
    public void PlausibleAdapterNamesAreAccepted(string name)
    {
        Assert.True(NetworkSettingsGuard.IsSafeAdapterName(name));
    }

    [Theory]
    [InlineData("0012")]
    [InlineData("4D36E972-E325-11CE-BFC1-08002BE10318")]
    [InlineData("{4D36E972-E325-11CE-BFC1-08002BE10318}")]
    public void PlausibleAdapterIdsAreAccepted(string id)
    {
        Assert.True(NetworkSettingsGuard.IsSafeAdapterId(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ethernet' OR '1'='1")]
    [InlineData("Eth; Start-Process calc")]
    [InlineData("Eth&whoami")]
    [InlineData("Eth$env:PATH")]
    [InlineData("a`b")]
    [InlineData("C:\\evil\\path")]
    [InlineData("a|b")]
    public void DangerousAdapterNamesAreRejected(string name)
    {
        Assert.False(NetworkSettingsGuard.IsSafeAdapterName(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Ethernet' OR '1'='1")]
    [InlineData("Eth; Start-Process calc")]
    [InlineData("Eth&whoami")]
    [InlineData("Eth$env:PATH")]
    [InlineData("a`b")]
    [InlineData("C:\\evil\\path")]
    [InlineData("a/b")]
    [InlineData("a|b")]
    public void DangerousAdapterIdsAreRejected(string id)
    {
        Assert.False(NetworkSettingsGuard.IsSafeAdapterId(id));
    }

    [Fact]
    public void NullAdapterNameAndIdAreRejected()
    {
        Assert.False(NetworkSettingsGuard.IsSafeAdapterName(null));
        Assert.False(NetworkSettingsGuard.IsSafeAdapterId(null));
    }

    [Fact]
    public void OverlongAdapterNameIsRejected()
    {
        string tooLong = new string('a', 300);
        Assert.False(NetworkSettingsGuard.IsSafeAdapterName(tooLong));
        Assert.False(NetworkSettingsGuard.IsSafeAdapterId(tooLong));
    }

    [Fact]
    public void SingleQuotesAreDoubledForPowerShell()
    {
        Assert.Equal("Eth''ernet", NetworkSettingsGuard.EscapePowerShellSingleQuoted("Eth'ernet"));
        Assert.Equal("Ethernet", NetworkSettingsGuard.EscapePowerShellSingleQuoted("Ethernet"));
        Assert.Equal("''''", NetworkSettingsGuard.EscapePowerShellSingleQuoted("''"));
    }
}
