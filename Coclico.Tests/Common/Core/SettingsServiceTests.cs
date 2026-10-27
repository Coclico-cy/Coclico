using System.Text.Json;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void DeserializeIgnoresUnknownLegacyKeys()
    {
        const string legacyJson = """
            {
              "language": "en",
              "startMaximized": false,
              "autoPatcherAuditOnly": true,
              "futureUnknownKey": { "nested": [1, 2, 3] }
            }
            """;

        AppSettings settings = JsonSerializer.Deserialize<AppSettings>(legacyJson) ?? new AppSettings();

        Assert.Equal("en", settings.Language);
        Assert.False(settings.StartMaximized);
    }

    [Fact]
    public void DeserializeFallsBackToDefaultsForMissingKeys()
    {
        const string partialJson = """{ "language": "en" }""";

        AppSettings settings = JsonSerializer.Deserialize<AppSettings>(partialJson) ?? new AppSettings();

        Assert.Equal("en", settings.Language);
        Assert.True(settings.StartMaximized);
        Assert.Equal("LocalGGUF", settings.AiProvider);
    }

    [Fact]
    public void SerializeDoesNotExposeRemovedSettings()
    {
        string json = JsonSerializer.Serialize(new AppSettings());

        Assert.DoesNotContain("autoPatcherAuditOnly", json);
        Assert.Contains("\"language\"", json);
        Assert.Contains("\"startMaximized\"", json);
    }

    [Fact]
    public void RoundTripPreservesKnownValues()
    {
        AppSettings original = new()
        {
            Language = "en",
            StartMaximized = false,
            FontSize = 15.5,
            ThemePreset = "Cyan"
        };

        string json = JsonSerializer.Serialize(original);
        AppSettings copy = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

        Assert.Equal(original.Language, copy.Language);
        Assert.Equal(original.StartMaximized, copy.StartMaximized);
        Assert.Equal(original.FontSize, copy.FontSize);
        Assert.Equal(original.ThemePreset, copy.ThemePreset);
    }
}
