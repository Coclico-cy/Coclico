using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class AiActionParserTests
{
    private const string Open = "<" + "think" + ">";
    private const string Close = "</" + "think" + ">";

    [Fact]
    public void Clean_RemovesThinkBlock()
    {
        string raw = Open + "raisonnement privé" + Close + "Réponse finale";
        Assert.Equal("Réponse finale", AiActionParser.Clean(raw));
    }

    [Fact]
    public void Clean_RemovesMultilineThinkBlock()
    {
        string raw = Open + "ligne 1\nligne 2\nligne 3" + Close + "\nRéponse utile";
        Assert.Equal("Réponse utile", AiActionParser.Clean(raw));
    }

    [Fact]
    public void Clean_RemovesMultipleThinkBlocks()
    {
        string raw = Open + "premier raisonnement" + Close + "Réponse A"
                   + Open + "second raisonnement" + Close + " et suite";
        Assert.Equal("Réponse A et suite", AiActionParser.Clean(raw));
    }

    [Fact]
    public void Clean_IsCaseInsensitiveOnTags()
    {
        string raw = Open.ToUpperInvariant() + "raisonnement" + Close.ToUpperInvariant() + "Réponse publique";
        Assert.Equal("Réponse publique", AiActionParser.Clean(raw));
    }

    [Fact]
    public void Clean_TrimsSurroundingWhitespace()
    {
        Assert.Equal("Bonjour", AiActionParser.Clean("   Bonjour   "));
    }

    [Fact]
    public void Clean_WithoutThinkBlock_ReturnsTrimmedInput()
    {
        Assert.Equal("Texte normal", AiActionParser.Clean("Texte normal"));
    }

    [Fact]
    public void Clean_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, AiActionParser.Clean(""));
        Assert.Equal(string.Empty, AiActionParser.Clean("   "));
    }

    [Fact]
    public void Clean_NullInput_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => AiActionParser.Clean(null!));
    }
}
