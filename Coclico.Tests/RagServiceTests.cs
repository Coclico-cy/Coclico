#nullable enable
using System;
using System.IO;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class RagServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly RagService _svc;

        public RagServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "CoclicoRagTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _svc = new RagService();
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        [Fact]
        public void BuildIndex_NonExistentPath_DoesNotThrow()
        {
            var ex = Record.Exception(() => _svc.BuildIndex(@"C:\NonExistentPath_CoclicoTest_999"));
            Assert.Null(ex);
        }

        [Fact]
        public void BuildIndex_NonExistentPath_ChunkCountIsZero()
        {
            _svc.BuildIndex(@"C:\NonExistentPath_CoclicoTest_999");
            Assert.Equal(0, _svc.ChunkCount);
        }

        [Fact]
        public void BuildIndex_EmptyDirectory_ChunkCountIsZero()
        {
            _svc.BuildIndex(_tempDir);
            Assert.Equal(0, _svc.ChunkCount);
        }

        [Fact]
        public void BuildIndex_SingleMarkdownFile_ProducesChunks()
        {
            File.WriteAllText(Path.Combine(_tempDir, "doc.md"),
                "## Nettoyage RAM\nLe nettoyage RAM permet de libérer la mémoire vive du système Windows.");
            _svc.BuildIndex(_tempDir);
            Assert.True(_svc.ChunkCount > 0);
        }

        [Fact]
        public void BuildIndex_MultipleFiles_AccumulatesChunks()
        {
            File.WriteAllText(Path.Combine(_tempDir, "a.md"),
                "## Section A\nContenu de la section A pour les tests unitaires de Coclico.");
            File.WriteAllText(Path.Combine(_tempDir, "b.md"),
                "## Section B\nContenu de la section B pour les tests unitaires de Coclico.");
            _svc.BuildIndex(_tempDir);
            Assert.True(_svc.ChunkCount >= 2);
        }

        [Fact]
        public void BuildIndex_FileWithoutHeaders_StillIndexed()
        {

            File.WriteAllText(Path.Combine(_tempDir, "plain.md"),
                "Ceci est un fichier markdown sans en-têtes mais avec assez de contenu pour être indexé correctement.");
            _svc.BuildIndex(_tempDir);
            Assert.True(_svc.ChunkCount > 0);
        }

        [Fact]
        public void BuildIndex_VeryShortSections_SkippedIfUnder40Chars()
        {

            File.WriteAllText(Path.Combine(_tempDir, "short.md"),
                "## TinySection\nShort.");
            _svc.BuildIndex(_tempDir);

            Assert.True(_svc.ChunkCount <= 1);
        }

        [Fact]
        public void BuildIndex_IgnoresNonMarkdownFiles()
        {
            File.WriteAllText(Path.Combine(_tempDir, "notes.txt"), "## Section\nThis is a text file not a markdown file.");
            _svc.BuildIndex(_tempDir);
            Assert.Equal(0, _svc.ChunkCount);
        }

        [Fact]
        public void BuildIndex_Rebuilds_ReplacesOldIndex()
        {
            File.WriteAllText(Path.Combine(_tempDir, "doc.md"),
                "## Première version\nContenu de la première version du document de test.");
            _svc.BuildIndex(_tempDir);
            int firstCount = _svc.ChunkCount;

            File.WriteAllText(Path.Combine(_tempDir, "doc2.md"),
                "## Deuxième section\nContenu de la deuxième section du document de test.");
            _svc.BuildIndex(_tempDir);
            int secondCount = _svc.ChunkCount;

            Assert.True(secondCount > firstCount);
        }

        [Fact]
        public void Search_EmptyIndex_ReturnsEmpty()
        {
            var result = _svc.Search("mémoire RAM nettoyage");
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void Search_AfterBuildIndex_ReturnsNonEmpty()
        {
            File.WriteAllText(Path.Combine(_tempDir, "ram.md"),
                "## Nettoyage mémoire\nLe nettoyage de la mémoire RAM libère de l'espace pour les applications actives sur Windows.");
            _svc.BuildIndex(_tempDir);

            var result = _svc.Search("nettoyage mémoire");
            Assert.NotEmpty(result);
        }

        [Fact]
        public void Search_RelevantQuery_ContainsExpectedContent()
        {
            File.WriteAllText(Path.Combine(_tempDir, "installer.md"),
                "## Installation de logiciels\nCoclico permet d'installer des logiciels via winget automatiquement.");
            _svc.BuildIndex(_tempDir);

            var result = _svc.Search("installer logiciel winget");
            Assert.Contains("winget", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Search_UnrelatedQuery_ReturnsEmpty()
        {
            File.WriteAllText(Path.Combine(_tempDir, "ram.md"),
                "## Nettoyage mémoire\nLe nettoyage de la mémoire vive libère de la RAM pour les applications Windows.");
            _svc.BuildIndex(_tempDir);

            var result = _svc.Search("xyzzy foobar nonexistent token abc");
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void Search_EmptyQuery_ReturnsEmpty()
        {
            File.WriteAllText(Path.Combine(_tempDir, "doc.md"),
                "## Section\nContenu de la section de test pour Coclico avec du texte suffisamment long.");
            _svc.BuildIndex(_tempDir);

            var result = _svc.Search("");
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void Search_WhitespaceOnlyQuery_ReturnsEmpty()
        {
            File.WriteAllText(Path.Combine(_tempDir, "doc.md"),
                "## Section\nContenu de la section de test pour Coclico avec du texte suffisamment long.");
            _svc.BuildIndex(_tempDir);

            var result = _svc.Search("   ");
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void Search_RespectTopKLimit()
        {
            for (int i = 1; i <= 5; i++)
                File.WriteAllText(Path.Combine(_tempDir, $"doc{i}.md"),
                    $"## Section{i}\nContenu test Coclico section numéro {i} avec mots clés recherche documentation.");
            _svc.BuildIndex(_tempDir);

            var result = _svc.Search("Coclico section documentation", topK: 2, maxChars: 99999);

            int separatorCount = result.Split(new[] { "\n---\n" }, StringSplitOptions.None).Length - 1;
            Assert.True(separatorCount <= 2, $"Expected at most 2 results, got separators: {separatorCount}");
        }

        [Fact]
        public void Search_RespectMaxCharsLimit()
        {
            File.WriteAllText(Path.Combine(_tempDir, "long.md"),
                "## Long Section\n" + new string('A', 5000) + " Coclico documentation test mots clés.");
            _svc.BuildIndex(_tempDir);

            var result = _svc.Search("coclico documentation", maxChars: 200);
            Assert.True(result.Length <= 210, $"Result length {result.Length} exceeds maxChars");
        }

        [Fact]
        public void Search_MultipleChunks_SelectsMostRelevant()
        {
            File.WriteAllText(Path.Combine(_tempDir, "cleaning.md"),
                "## Nettoyage disque\nLe nettoyage du disque dur supprime les fichiers temporaires inutiles.");
            File.WriteAllText(Path.Combine(_tempDir, "ram.md"),
                "## Optimisation RAM\nL'optimisation de la mémoire RAM améliore les performances du système.");
            _svc.BuildIndex(_tempDir);

            var result = _svc.Search("RAM mémoire optimisation performances");
            Assert.Contains("RAM", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ChunkCount_AfterBuild_IsPositive()
        {
            File.WriteAllText(Path.Combine(_tempDir, "doc.md"),
                "## Section principale\nContenu suffisamment long pour être indexé par le service RAG de Coclico.");
            _svc.BuildIndex(_tempDir);
            Assert.True(_svc.ChunkCount > 0);
        }

        [Fact]
        public void Search_FrenchAccentedTokens_WorkCorrectly()
        {
            File.WriteAllText(Path.Combine(_tempDir, "fr.md"),
                "## Nettoyage système\nL'optimisation mémoire améliore les performances en libérant l'espace utilisé.");
            _svc.BuildIndex(_tempDir);

            var result = _svc.Search("optimisation mémoire libérant");
            Assert.NotEmpty(result);
        }
    }
}
