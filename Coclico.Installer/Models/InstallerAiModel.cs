using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Coclico.Installer.Models;

public sealed record InstallerAiModel(
    string Id,
    string DisplayName,
    string DescriptionFr,
    string DescriptionEn,
    long SizeMb,
    string DownloadUrl,
    bool IsRecommended = false,
    string? ExpectedSha256 = null)
{
    public override string ToString() => DisplayName;
}

public static class InstallerAiCatalog
{
    private static readonly IReadOnlyList<InstallerAiModel> FallbackModels =
    [
        new(
            Id: "IA-support-chat.gguf",
            DisplayName: "Coclico Tuned (1.5B)",
            DescriptionFr: "Modèle compact optimisé pour le support et les commandes Coclico (~1.1 Go)",
            DescriptionEn: "Compact model tuned for Coclico support and commands (~1.1 GB)",
            SizeMb: 1100,
            DownloadUrl: "https://huggingface.co/kiwifrite/Assistant_ia_chat/resolve/main/IA-support-chat.gguf",
            IsRecommended: true),
        new(
            Id: "Llama-3.2-1B-Instruct-Q4_K_M.gguf",
            DisplayName: "Llama 3.2 (1B Ultra-léger)",
            DescriptionFr: "Ultra-rapide, consomme moins de 1 Go de mémoire RAM (~800 Mo)",
            DescriptionEn: "Ultra-fast, consumes less than 1 GB of RAM (~800 MB)",
            SizeMb: 800,
            DownloadUrl: "https://huggingface.co/bartowski/Llama-3.2-1B-Instruct-GGUF/resolve/main/Llama-3.2-1B-Instruct-Q4_K_M.gguf"),
        new(
            Id: "Qwen2.5-1.5B-Instruct-Q4_K_M.gguf",
            DisplayName: "Qwen 2.5 (1.5B Equilibré)",
            DescriptionFr: "Excellent en français et en code, léger (~1.1 Go)",
            DescriptionEn: "Excellent in French and code, lightweight (~1.1 GB)",
            SizeMb: 1100,
            DownloadUrl: "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf"),
        new(
            Id: "Llama-3.2-3B-Instruct-Q4_K_M.gguf",
            DisplayName: "Llama 3.2 (3B Avancé)",
            DescriptionFr: "Très précis, requiert ~2.5 Go de mémoire RAM (~2.0 Go)",
            DescriptionEn: "Very accurate, requires ~2.5 GB of RAM (~2.0 GB)",
            SizeMb: 2000,
            DownloadUrl: "https://huggingface.co/bartowski/Llama-3.2-3B-Instruct-GGUF/resolve/main/Llama-3.2-3B-Instruct-Q4_K_M.gguf")
    ];

    public static IReadOnlyList<InstallerAiModel> Models { get; } = LoadFromEmbeddedJson() ?? FallbackModels;

    private static IReadOnlyList<InstallerAiModel>? LoadFromEmbeddedJson()
    {
        try
        {
            Assembly assembly = typeof(InstallerAiCatalog).Assembly;
            const string resourceName = "Coclico.Installer.Assets.ai-models.json";
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(stream);
            List<InstallerAiModel> models = [];
            foreach (JsonElement element in doc.RootElement.GetProperty("Models").EnumerateArray())
            {
                models.Add(new InstallerAiModel(
                    Id: element.GetProperty("Id").GetString()!,
                    DisplayName: element.GetProperty("DisplayName").GetString()!,
                    DescriptionFr: element.GetProperty("DescriptionFr").GetString()!,
                    DescriptionEn: element.GetProperty("DescriptionEn").GetString()!,
                    SizeMb: element.GetProperty("SizeMb").GetInt64(),
                    DownloadUrl: element.GetProperty("DownloadUrl").GetString()!,
                    IsRecommended: element.TryGetProperty("IsRecommended", out JsonElement rec) && rec.GetBoolean(),
                    ExpectedSha256: element.TryGetProperty("ExpectedSha256", out JsonElement sha) ? sha.GetString() : null));
            }

            return models.Count > 0 ? models : null;
        }
        catch
        {
            return null;
        }
    }
}