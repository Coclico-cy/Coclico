#nullable enable
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Coclico.Services;

public interface IAiService
{
    bool IsInitialized { get; }

    Task InitializeAsync(CancellationToken ct = default);

    IAsyncEnumerable<string> SendMessageAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default);

    IAsyncEnumerable<string> SendSystemPromptAsync(
        string systemPrompt,
        [EnumeratorCancellation] CancellationToken ct = default);

    void RecordExchange(string userMsg, string aiResponse);
    void ResetConversation();
    string CurrentStatusContext { get; set; }
}
