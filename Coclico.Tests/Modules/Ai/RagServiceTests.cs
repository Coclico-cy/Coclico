using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class RagServiceTests : IDisposable
{
    private readonly string _docsDir;

    public RagServiceTests()
    {
        _docsDir = Path.Join(Path.GetTempPath(), "coclico-rag-tests-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_docsDir);

        File.WriteAllText(Path.Join(_docsDir, "memory.md"),
            "# Memoire vive\n\nLa memoire vive (RAM) stocke les donnees des applications en cours d'execution. " +
            "Vider la RAM suspend les processus en arriere-plan et libere du working set.");
        File.WriteAllText(Path.Join(_docsDir, "network.md"),
            "# Reseau\n\nLe reseau connecte l'ordinateur a internet. La latence (ping) mesure le temps de reponse. " +
            "Le DNS traduit les noms de domaine en adresses IP.");
        File.WriteAllText(Path.Join(_docsDir, "disk.md"),
            "# Disque dur\n\nLe disque dur stocke les fichiers. L'analyseur de disque recherche les gros fichiers " +
            "et les doublons qui occupent de l'espace.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_docsDir, recursive: true); } catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }
    }

    [Fact]
    public void BuildIndex_LoadsChunksFromMarkdown()
    {
        RagService rag = new();
        rag.BuildIndex(_docsDir);

        Assert.True(rag.ChunkCount > 0);
    }

    [Fact]
    public void BuildIndex_MissingDirectory_IsNoop()
    {
        RagService rag = new();
        rag.BuildIndex(Path.Join(Path.GetTempPath(), "coclico-rag-missing-" + Guid.NewGuid().ToString("N")));

        Assert.Equal(0, rag.ChunkCount);
    }

    [Fact]
    public void Search_ReturnsRelevantChunk()
    {
        RagService rag = new();
        rag.BuildIndex(_docsDir);

        string result = rag.Search("comment vider la memoire vive", topK: 1);

        Assert.Contains("RAM", result, StringComparison.Ordinal);
        Assert.DoesNotContain("DNS", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_RanksTopicalDocumentFirst()
    {
        RagService rag = new();
        rag.BuildIndex(_docsDir);

        string result = rag.Search("latence ping dns reseau", topK: 1);

        Assert.Contains("reseau", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_EmptyIndex_ReturnsEmpty()
    {
        RagService rag = new();

        Assert.Equal(string.Empty, rag.Search("memoire"));
    }

    [Fact]
    public void Search_NoStopWordQuery_ReturnsEmpty()
    {
        RagService rag = new();
        rag.BuildIndex(_docsDir);

        Assert.Equal(string.Empty, rag.Search("le la les de du"));
    }

    [Fact]
    public void Search_RespectsMaxCharsBudget()
    {
        RagService rag = new();
        rag.BuildIndex(_docsDir);

        string result = rag.Search("memoire reseau disque", topK: 3, maxChars: 200);

        Assert.True(result.Length <= 210, $"Result was {result.Length} chars");
    }
}
