using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class UninstallCommandParserTests
{
    [Fact]
    public void Parse_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(UninstallCommandParser.Parse(null));
        Assert.Null(UninstallCommandParser.Parse("   "));
    }

    [Theory]
    [InlineData("steam://run/123")]
    [InlineData("com.epicgames.launcher://apps/xyz?action=uninstall")]
    [InlineData("ms-settings:appsfeatures")]
    public void Parse_KnownUriSchemes_IsUri(string command)
    {
        ParsedUninstallCommand? parsed = UninstallCommandParser.Parse(command);

        Assert.NotNull(parsed);
        Assert.True(parsed.IsUri);
        Assert.False(parsed.IsMsi);
        Assert.Equal(command.Trim(), parsed.ExecutableOrUri);
    }

    [Theory]
    [InlineData("ms-settings:appsfeatures;rm -rf /")]
    [InlineData("ms-settings:")]
    public void Parse_MalformedSettingsUri_ReturnsNull(string command)
    {
        Assert.Null(UninstallCommandParser.Parse(command));
    }

    [Theory]
    [InlineData("msiexec.exe /x {11111111-2222-3333-4444-555555555555}", "/x {11111111-2222-3333-4444-555555555555}")]
    [InlineData("MsiExec /I{11111111-2222-3333-4444-555555555555}", "/I{11111111-2222-3333-4444-555555555555}")]
    [InlineData("msiexec /uninstall {11111111-2222-3333-4444-555555555555}", "/uninstall {11111111-2222-3333-4444-555555555555}")]
    public void Parse_MsiUninstall_IsMsiWithArgs(string command, string expectedArgs)
    {
        ParsedUninstallCommand? parsed = UninstallCommandParser.Parse(command);

        Assert.NotNull(parsed);
        Assert.True(parsed.IsMsi);
        Assert.EndsWith("msiexec.exe", parsed.ExecutableOrUri, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedArgs, parsed.Arguments);
    }

    [Theory]
    [InlineData("msiexec /y somedll.dll")]
    [InlineData("msiexec /a package.msi")]
    public void Parse_MsiNonUninstallMode_ReturnsNull(string command)
    {
        Assert.Null(UninstallCommandParser.Parse(command));
    }

    [Fact]
    public void Parse_QuotedPath_SplitsExeAndArgs()
    {
        string exe = Path.Combine(Path.GetTempPath(), "unins_test.exe");
        File.WriteAllText(exe, "stub");

        try
        {
            ParsedUninstallCommand? parsed = UninstallCommandParser.Parse($"\"{exe}\" /SILENT");

            Assert.NotNull(parsed);
            Assert.Equal(exe, parsed.ExecutableOrUri);
            Assert.Equal("/SILENT", parsed.Arguments);
        }
        finally
        {
            File.Delete(exe);
        }
    }

    [Fact]
    public void Parse_UnquotedWithArgs_FindsExePath()
    {
        string exe = Path.Combine(Path.GetTempPath(), "unins_quoted_test.exe");
        File.WriteAllText(exe, "stub");

        try
        {
            ParsedUninstallCommand? parsed = UninstallCommandParser.Parse($"{exe} /VERYSILENT /NORESTART");

            Assert.NotNull(parsed);
            Assert.Equal(exe, parsed.ExecutableOrUri);
            Assert.Equal("/VERYSILENT /NORESTART", parsed.Arguments);
        }
        finally
        {
            File.Delete(exe);
        }
    }

    [Fact]
    public void Parse_NoArguments_SingleToken()
    {
        ParsedUninstallCommand? parsed = UninstallCommandParser.Parse("SomeAppUninstaller");

        Assert.NotNull(parsed);
        Assert.Equal("SomeAppUninstaller", parsed.ExecutableOrUri);
        Assert.Equal(string.Empty, parsed.Arguments);
    }
}