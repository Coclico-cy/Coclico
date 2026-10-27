using Coclico.Services.Algorithms;
using Xunit;

namespace Coclico.Tests;

public sealed class SimpleStemmerTests
{
    [Theory]
    [InlineData("nettoyage", "nettoy")]
    [InlineData("installation", "install")]
    [InlineData("installations", "install")]
    [InlineData("optimisation", "optimis")]
    [InlineData("universaux", "universal")]
    [InlineData("chateaux", "chateau")]
    public void Stem_RemovesKnownSuffixes(string word, string expected)
    {
        Assert.Equal(expected, SimpleStemmer.Stem(word));
    }

    [Fact]
    public void Stem_PreservesShortWords()
    {
        Assert.Equal("abc", SimpleStemmer.Stem("abc"));
        Assert.Equal("de", SimpleStemmer.Stem("de"));
    }

    [Fact]
    public void Stem_KeepsWordWhenRootWouldBeTooShort()
    {
        Assert.Equal("page", SimpleStemmer.Stem("page"));
    }

    [Theory]
    [InlineData("le")]
    [InlineData("les")]
    [InlineData("the")]
    [InlineData("with")]
    [InlineData("et")]
    [InlineData("ab")]
    public void IsStopWord_DetectsCommonWords(string word)
    {
        Assert.True(SimpleStemmer.IsStopWord(word));
    }

    [Theory]
    [InlineData("réglage")]
    [InlineData("système")]
    [InlineData("cleaner")]
    public void IsStopWord_DoesNotFlagMeaningfulWords(string word)
    {
        Assert.False(SimpleStemmer.IsStopWord(word));
    }

    [Fact]
    public void IsStopWord_IsCaseInsensitive()
    {
        Assert.True(SimpleStemmer.IsStopWord("LE"));
        Assert.True(SimpleStemmer.IsStopWord("The"));
    }
}
