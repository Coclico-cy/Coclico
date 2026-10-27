using System.Text.Json;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class CustomShortcutJsonTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void Serialize_UsesStableCamelCaseContract()
    {
        var shortcut = new CustomShortcut
        {
            Title = "Notepad",
            Subtitle = "Éditeur de texte",
            Type = ShortcutType.WindowsTool,
            Target = "notepad.exe",
            IconSymbol = "Edit24",
            AccentColor = "#FF0000",
            BgTint = "#101010"
        };

        string json = JsonSerializer.Serialize(shortcut);

        Assert.Contains("\"id\"", json);
        Assert.Contains("\"title\"", json);
        Assert.Contains("\"subtitle\"", json);
        Assert.Contains("\"type\"", json);
        Assert.Contains("\"target\"", json);
        Assert.Contains("\"iconSymbol\"", json);
        Assert.Contains("\"accentColor\"", json);
        Assert.Contains("\"bgTint\"", json);
    }

    [Fact]
    public void RoundTrip_PreservesAllValues()
    {
        var original = new CustomShortcut
        {
            Title = "Nettoyage",
            Subtitle = "Libère de l'espace",
            Type = ShortcutType.CoclicoAction,
            Target = "coclico://clean",
            IconSymbol = "Broom24",
            AccentColor = "#00FF00",
            BgTint = "#0A0F1E"
        };

        string json = JsonSerializer.Serialize(original);
        var readBack = JsonSerializer.Deserialize<CustomShortcut>(json);

        Assert.NotNull(readBack);
        Assert.Equal(original.Id, readBack.Id);
        Assert.Equal(original.Title, readBack.Title);
        Assert.Equal(original.Subtitle, readBack.Subtitle);
        Assert.Equal(original.Type, readBack.Type);
        Assert.Equal(original.Target, readBack.Target);
        Assert.Equal(original.IconSymbol, readBack.IconSymbol);
        Assert.Equal(original.AccentColor, readBack.AccentColor);
        Assert.Equal(original.BgTint, readBack.BgTint);
    }

    [Fact]
    public void Deserialize_AcceptsLegacyJson()
    {
        const string json = """
            {
              "id": "abc123",
              "title": "Ancien raccourci",
              "subtitle": "importé",
              "type": 2,
              "target": "C:\\outils\\monoutil.exe",
              "iconSymbol": "Apps24",
              "accentColor": "#6366F1",
              "bgTint": "#13182E"
            }
            """;

        var shortcut = JsonSerializer.Deserialize<CustomShortcut>(json);

        Assert.NotNull(shortcut);
        Assert.Equal("abc123", shortcut.Id);
        Assert.Equal("Ancien raccourci", shortcut.Title);
        Assert.Equal(ShortcutType.CustomApp, shortcut.Type);
        Assert.Equal(@"C:\outils\monoutil.exe", shortcut.Target);
    }

    [Fact]
    public void NewInstances_GetUniqueNonEmptyIds()
    {
        var first = new CustomShortcut();
        var second = new CustomShortcut();

        Assert.False(string.IsNullOrWhiteSpace(first.Id));
        Assert.False(string.IsNullOrWhiteSpace(second.Id));
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void ShortcutType_HasTheThreeExpectedValues()
    {
        Assert.Equal(0, (int)ShortcutType.CoclicoAction);
        Assert.Equal(1, (int)ShortcutType.WindowsTool);
        Assert.Equal(2, (int)ShortcutType.CustomApp);
    }
}
