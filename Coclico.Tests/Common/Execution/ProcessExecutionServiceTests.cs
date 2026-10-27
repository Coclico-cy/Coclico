using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class ProcessExecutionServiceTests
{
    [Theory]
    [InlineData("--version", "--version")]
    [InlineData("", "\"\"")]
    [InlineData("C:\\Program Files\\App\\app.exe", "\"C:\\Program Files\\App\\app.exe\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("C:\\dir\\", "\"C:\\dir\\\\\"")]
    [InlineData("a\\\"b\\", "\"a\\\\\\\"b\\\\\"")]
    public void QuoteArgumentFollowsWindowsArgvRules(string input, string expected)
    {
        Assert.Equal(expected, ProcessExecutionService.QuoteArgument(input));
    }

    [Fact]
    public void QuoteArgumentKeepsSimpleArgumentsUntouched()
    {
        Assert.Equal("/silent", ProcessExecutionService.QuoteArgument("/silent"));
        Assert.Equal("--id", ProcessExecutionService.QuoteArgument("--id"));
    }

    [Fact]
    public void QuoteArgumentWrapsArgumentsContainingSpaces()
    {
        string quoted = ProcessExecutionService.QuoteArgument("Mozilla Firefox");
        Assert.Equal("\"Mozilla Firefox\"", quoted);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("--silent", "--silent")]
    [InlineData("--silent --verbose", "--silent --verbose")]
    [InlineData("C:\\a b\\file.txt", "C:\\a b\\file.txt")]
    public void RequoteArgumentsPreservesWellFormedInput(string? input, string expected)
    {
        Assert.Equal(expected, ProcessExecutionService.RequoteArguments(input));
    }

    [Fact]
    public void RequoteArgumentsKeepsQuotedArgumentWithSpacesAsSingleToken()
    {
        string result = ProcessExecutionService.RequoteArguments("\"a b.txt\" --out");
        Assert.Equal("\"a b.txt\" --out", result);
    }

    [Fact]
    public void RequoteArgumentsNeutralizesUnbalancedQuotes()
    {
        string result = ProcessExecutionService.RequoteArguments("a\"b c\"d");
        Assert.Equal("\"ab cd\"", result);
    }
}
