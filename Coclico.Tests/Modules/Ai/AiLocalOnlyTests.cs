using Coclico.Services;
using Coclico.Services.AI;
using Xunit;

namespace Coclico.Tests;

public sealed class AiLocalOnlyTests
{
    [Fact]
    public void ProviderCatalogContainsOnlyLocalOptions()
    {
        Assert.Equal(new[] { AiProviderType.LocalGGUF, AiProviderType.Ollama }, Enum.GetValues<AiProviderType>());
        Assert.All(AiModelCatalog.AllModels, model => Assert.Contains(model.Provider, Enum.GetValues<AiProviderType>()));
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Gemini")]
    [InlineData("Groq")]
    [InlineData("Anthropic")]
    [InlineData("99")]
    public void RemovedProvidersMigrateToTheLocalModel(string savedProvider)
    {
        Assert.Equal(AiProviderType.LocalGGUF, AiChatService.ResolveSupportedProvider(savedProvider));
    }

    [Fact]
    public void RemovedCloudModelMigratesToTheDefaultLocalModel()
    {
        Assert.Equal(
            AiModelCatalog.GetDefaultModel(AiProviderType.LocalGGUF).Id,
            AiChatService.ResolveSupportedModel(AiProviderType.LocalGGUF, "gpt-4o"));
    }

    [Theory]
    [InlineData("http://localhost:11434", true)]
    [InlineData("http://127.0.0.1:11434", true)]
    [InlineData("http://[::1]:11434", true)]
    [InlineData("https://api.example.com", false)]
    [InlineData("http://192.168.1.10:11434", false)]
    [InlineData("http://user@localhost:11434", false)]
    [InlineData("http://localhost:11434/api", false)]
    [InlineData("not a url", false)]
    public void OllamaEndpointMustBeLoopbackOnly(string endpoint, bool expected)
    {
        Assert.Equal(expected, MultiProviderClient.IsLocalEndpoint(endpoint));
    }

    [Fact]
    public async Task DestructiveAiActionsRequireConfirmationAtServiceBoundary()
    {
        var client = new AiToolExecutionService();
        var action = new AiActionRequest("CLEAN_TEMP", null, "[ACTION:CLEAN_TEMP]");

        AiActionResult result = await client.ExecuteActionAsync(action);

        Assert.False(result.Success);
        Assert.Contains("Confirmation", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
