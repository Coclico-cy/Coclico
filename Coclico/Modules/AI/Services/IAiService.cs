using Coclico.Services.AI;

namespace Coclico.Services;

public interface IAiService
{
    bool IsInitialized { get; }
    bool IsEnabled { get; }
    bool UseGpu { get; }

    AiProviderType CurrentProvider { get; set; }
    string CurrentModel { get; set; }

    Task InitializeAsync(CancellationToken ct = default);
    Task SwitchProviderAsync(AiProviderType provider, string? model = null);
    Task UnloadAsync();
    Task SetEnabledAsync(bool enabled);
    Task SetHardwareModeAsync(bool useGpu, int layers = 32);

    IAsyncEnumerable<string> SendMessageAsync(
        string userMessage,
        CancellationToken ct = default);

    IAsyncEnumerable<string> SendMessageWithVisionAsync(
        string userMessage,
        string? base64Image = null,
        string? imageMimeType = null,
        CancellationToken ct = default);

    void RecordExchange(string userMsg, string aiResponse);
    void ResetConversation();
    string CurrentStatusContext { get; set; }
}
