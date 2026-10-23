using System.IO;
namespace Coclico.Services.AI;

public enum AiProviderType
{
    LocalGGUF,
    Ollama
}

public sealed record AiModelInfo(
    string Id,
    string DisplayName,
    AiProviderType Provider,
    string Description,
    bool SupportsVision = false,
    long SizeEstimateMb = 0,
    string? DownloadUrl = null,
    bool IsRecommended = false);

public static class AiModelCatalog
{
    private static readonly IReadOnlyList<AiModelInfo> DefaultModels =
    [
        new(
            Id: "IA-support-chat.gguf",
            DisplayName: "Coclico Tuned (1.5B)",
            Provider: AiProviderType.LocalGGUF,
            Description: "Modèle compact optimisé pour le support et les commandes Coclico",
            SupportsVision: false,
            SizeEstimateMb: 1100,
            DownloadUrl: "https://huggingface.co/kiwifrite/Assistant_ia_chat/resolve/main/IA-support-chat.gguf",
            IsRecommended: true),
        new(
            Id: "Llama-3.2-1B-Instruct-Q4_K_M.gguf",
            DisplayName: "Llama 3.2 (1B Ultra-léger)",
            Provider: AiProviderType.LocalGGUF,
            Description: "Ultra-rapide, consomme moins de 1 Go de RAM",
            SupportsVision: false,
            SizeEstimateMb: 800,
            DownloadUrl: "https://huggingface.co/bartowski/Llama-3.2-1B-Instruct-GGUF/resolve/main/Llama-3.2-1B-Instruct-Q4_K_M.gguf"),
        new(
            Id: "Qwen2.5-1.5B-Instruct-Q4_K_M.gguf",
            DisplayName: "Qwen 2.5 (1.5B Equilibré)",
            Provider: AiProviderType.LocalGGUF,
            Description: "Excellent en français et en code, léger (~1.1 Go)",
            SupportsVision: false,
            SizeEstimateMb: 1100,
            DownloadUrl: "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf"),
        new(
            Id: "Llama-3.2-3B-Instruct-Q4_K_M.gguf",
            DisplayName: "Llama 3.2 (3B Avancé)",
            Provider: AiProviderType.LocalGGUF,
            Description: "Très précis, requiert ~2.5 Go de RAM",
            SupportsVision: false,
            SizeEstimateMb: 2000,
            DownloadUrl: "https://huggingface.co/bartowski/Llama-3.2-3B-Instruct-GGUF/resolve/main/Llama-3.2-3B-Instruct-Q4_K_M.gguf"),
        new(
            Id: "llama3.2",
            DisplayName: "Ollama — Llama 3.2",
            Provider: AiProviderType.Ollama,
            Description: "Serveur local Ollama (port 11434)",
            SupportsVision: true,
            IsRecommended: true),
        new(
            Id: "qwen2.5",
            DisplayName: "Ollama — Qwen 2.5",
            Provider: AiProviderType.Ollama,
            Description: "Serveur local Ollama (port 11434)",
            SupportsVision: false),
        new(
            Id: "mistral",
            DisplayName: "Ollama — Mistral",
            Provider: AiProviderType.Ollama,
            Description: "Serveur local Ollama (port 11434)",
            SupportsVision: false),
    ];

    private static readonly Lazy<IReadOnlyList<AiModelInfo>> _loaded = new(LoadModels);

    public static IReadOnlyList<AiModelInfo> AllModels => _loaded.Value;

    private static IReadOnlyList<AiModelInfo> LoadModels()
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Coclico", "ai-models.json");
            if (!File.Exists(path))
            {
                return DefaultModels;
            }

            string json = File.ReadAllText(path);
            var extra = System.Text.Json.JsonSerializer.Deserialize<List<AiModelInfo>>(json,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });
            if (extra is not { Count: > 0 })
            {
                return DefaultModels;
            }

            var merged = new List<AiModelInfo>(DefaultModels);
            foreach (AiModelInfo m in extra)
            {
                int index = merged.FindIndex(x => x.Id == m.Id);
                if (index >= 0)
                {
                    merged[index] = m;
                }
                else
                {
                    merged.Add(m);
                }
            }

            LoggingService.LogInfo($"[AiModelCatalog] {extra.Count} modele(s) charge(s) depuis {path}");
            return merged;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AiModelCatalog.LoadModels");
            return DefaultModels;
        }
    }

    public static IReadOnlyList<AiModelInfo> GetModelsForProvider(AiProviderType provider)
    {
        return AllModels.Where(m => m.Provider == provider).ToList();
    }

    public static AiModelInfo GetDefaultModel(AiProviderType provider)
    {
        return AllModels.FirstOrDefault(m => m.Provider == provider && m.IsRecommended)
        ?? AllModels.First(m => m.Provider == provider);
    }
}
