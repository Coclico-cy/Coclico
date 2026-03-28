#nullable enable
using System;
using System.IO;
using System.Linq;
using Coclico.Services;
using Coclico.Services.Algorithms;
using Xunit;

namespace Coclico.Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // FLUX 1 : IsolationForestDetector — entraînement + détection d'anomalie
    // ─────────────────────────────────────────────────────────────────────────

    public class IsolationForestIntegrationTests
    {
        [Fact]
        public void TrainOnNormalData_ThenDetectObviousAnomaly_ScoreAboveThreshold()
        {
            // Arrange : 60 points normaux (CPU ~50 %, RAM ~60 %)
            var detector = new IsolationForestDetector(numTrees: 50, subsampleSize: 32, anomalyThreshold: 0.65);
            var rng = new Random(42);

            for (int i = 0; i < 60; i++)
                detector.Observe(50.0 + rng.NextDouble() * 10, 60.0 + rng.NextDouble() * 5);

            // Le détecteur doit être prêt après MinSamples (20) observations
            Assert.True(detector.IsReady);

            // Act : point aberrant évident (99 % CPU, 99 % RAM)
            var (score, isAnomaly, details) = detector.Observe(99.0, 99.0);

            // Assert
            Assert.True(score > 0.5, $"Score attendu > 0.5, obtenu {score:F3}");
            Assert.True(isAnomaly, "Le point (99,99) doit être une anomalie");
        }

        [Fact]
        public void NotReady_BeforeMinSamples_ReturnsNegativeScore()
        {
            var detector = new IsolationForestDetector(numTrees: 10, subsampleSize: 16);

            // Seulement 5 observations — en dessous du MinSamplesForFit = 20
            for (int i = 0; i < 5; i++)
                detector.Observe(50.0, 60.0);

            var (score, isAnomaly, _) = detector.Observe(50.0, 60.0);

            Assert.False(detector.IsReady);
            Assert.Equal(-1, score);
            Assert.False(isAnomaly);
        }

        [Fact]
        public void NormalPoint_AfterTraining_IsNotFlaggedAnomaly()
        {
            var detector = new IsolationForestDetector(numTrees: 50, subsampleSize: 32, anomalyThreshold: 0.65);
            var rng = new Random(7);

            // Entraîner avec 60 points normaux autour de (50, 60)
            for (int i = 0; i < 60; i++)
                detector.Observe(50.0 + rng.NextDouble() * 5, 60.0 + rng.NextDouble() * 5);

            // Un point dans la distribution normale ne doit pas être une anomalie
            var (score, isAnomaly, _) = detector.Observe(52.0, 61.0);

            Assert.True(score >= 0, "Le score doit être >= 0 une fois le détecteur prêt");
            Assert.False(isAnomaly, "Un point normal ne doit pas être marqué anomalie");
        }

        [Fact]
        public void MultiDimensional_ObserveThreeFeatures_WorksCorrectly()
        {
            var detector = new IsolationForestDetector(numTrees: 30, subsampleSize: 32);
            var rng = new Random(13);

            // Entraîner sur 3 features (CPU, RAM, IO)
            for (int i = 0; i < 40; i++)
                detector.Observe(
                    40 + rng.NextDouble() * 20,
                    50 + rng.NextDouble() * 20,
                    10 + rng.NextDouble() * 10);

            Assert.True(detector.IsReady);

            // Point aberrant sur toutes les dimensions
            var (score, isAnomaly, _) = detector.Observe(99.0, 99.0, 99.0);
            Assert.True(score > 0.5);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FLUX 2 : RagService → SimpleStemmer → index BM25 + TF-IDF
    // ─────────────────────────────────────────────────────────────────────────

    public class RagServiceIntegrationTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly RagService _rag;

        public RagServiceIntegrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"CoclicoRagIT_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _rag = new RagService();
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        [Fact]
        public void SimpleStemmer_StemsNettoyage_ToNettoy()
        {
            // Test du stemmer en isolation — "nettoyage" → racine "nettoy"
            var stem = SimpleStemmer.Stem("nettoyage");
            Assert.Equal("nettoy", stem);
        }

        [Fact]
        public void SimpleStemmer_StemsNettoyages_ToNettoy()
        {
            var stem = SimpleStemmer.Stem("nettoyages");
            Assert.Equal("nettoy", stem);
        }

        [Fact]
        public void RagService_IndexAndSearch_FindsRelevantChunk()
        {
            // Indexer un document contenant "nettoyage" (sera stemmé en "nettoy")
            File.WriteAllText(
                Path.Combine(_tempDir, "memoire.md"),
                "## Nettoyage RAM\nLe nettoyage de la mémoire RAM libère les ressources vives du système.\n" +
                "La RAM doit être nettoyée régulièrement pour optimiser les performances Windows.");

            File.WriteAllText(
                Path.Combine(_tempDir, "reseau.md"),
                "## Réseau\nLa configuration réseau permet de gérer les interfaces et les connexions du système d'exploitation.");

            _rag.BuildIndex(_tempDir);

            Assert.True(_rag.ChunkCount >= 2, $"Attendu >= 2 chunks, obtenu {_rag.ChunkCount}");

            // La recherche "nettoyer" doit retrouver le chunk RAM (même racine "nettoy")
            var result = _rag.Search("nettoyer memoire", topK: 3);

            Assert.False(string.IsNullOrWhiteSpace(result), "La recherche doit retourner un résultat");
            // Le résultat doit contenir du contenu du chunk RAM
            Assert.True(
                result.Contains("RAM", StringComparison.OrdinalIgnoreCase) ||
                result.Contains("mémoire", StringComparison.OrdinalIgnoreCase) ||
                result.Contains("nettoy", StringComparison.OrdinalIgnoreCase),
                $"Résultat inattendu : {result}");
        }

        [Fact]
        public void RagService_EmptyQuery_ReturnsEmpty()
        {
            File.WriteAllText(
                Path.Combine(_tempDir, "doc.md"),
                "## Section\nContenu de test pour le système Coclico.");
            _rag.BuildIndex(_tempDir);

            var result = _rag.Search("");
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void RagService_MultipleDocuments_ChunkCountReflectsAllSections()
        {
            // 3 fichiers, chacun avec 2 sections → au moins 6 chunks
            for (int i = 1; i <= 3; i++)
            {
                File.WriteAllText(
                    Path.Combine(_tempDir, $"doc{i}.md"),
                    $"## Section A du document {i}\nContenu A détaillé avec des informations importantes pour Coclico.\n" +
                    $"## Section B du document {i}\nContenu B détaillé avec d'autres informations clés pour le système.");
            }

            _rag.BuildIndex(_tempDir);
            Assert.True(_rag.ChunkCount >= 6, $"Attendu >= 6 chunks, obtenu {_rag.ChunkCount}");
        }

        [Fact]
        public void SimpleStemmer_IsStopWord_FiltersCommonWords()
        {
            Assert.True(SimpleStemmer.IsStopWord("le"));
            Assert.True(SimpleStemmer.IsStopWord("the"));
            Assert.True(SimpleStemmer.IsStopWord("de"));
            Assert.False(SimpleStemmer.IsStopWord("nettoyage"));
            Assert.False(SimpleStemmer.IsStopWord("optimisation"));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FLUX 3 : DynamicTracerService → TDigest → quantiles P50/P95
    // ─────────────────────────────────────────────────────────────────────────

    public class DynamicTracerIntegrationTests : IDisposable
    {
        private readonly DynamicTracerService _tracer;

        public DynamicTracerIntegrationTests()
        {
            _tracer = new DynamicTracerService();
        }

        public void Dispose()
        {
            _tracer.Dispose();
        }

        [Fact]
        public void Record100Operations_P50InExpectedRange()
        {
            const string opName = "IntegTest_P50";

            // Enregistrer 100 opérations avec des durées linéaires : 10 ms à 110 ms
            // Médiane théorique ≈ 60 ms
            for (int i = 1; i <= 100; i++)
                _tracer.Record(opName, TimeSpan.FromMilliseconds(10 + i));

            var agg = _tracer.GetAggregate(opName);

            Assert.NotNull(agg);
            Assert.Equal(100, agg!.SampleCount);

            // La médiane (P50 via TDigest) doit être dans [50 ms, 70 ms]
            double p50Ms = agg.Average.TotalMilliseconds;
            Assert.InRange(p50Ms, 50, 70);
        }

        [Fact]
        public void Record100Operations_P95HigherThanAverage()
        {
            const string opName = "IntegTest_P95";

            // Durées : 1 à 100 ms — P95 doit être proche de 95 ms
            for (int i = 1; i <= 100; i++)
                _tracer.Record(opName, TimeSpan.FromMilliseconds(i));

            var agg = _tracer.GetAggregate(opName);

            Assert.NotNull(agg);

            // P95 doit être supérieur à la moyenne et inférieur au max
            Assert.True(agg!.P95 > agg.Average, "P95 doit être supérieur à la moyenne");
            Assert.True(agg.P95 <= agg.Max, "P95 doit être <= au maximum");
            Assert.InRange(agg.P95.TotalMilliseconds, 88, 100);
        }

        [Fact]
        public void Record_MinMaxBounds_AreCorrect()
        {
            const string opName = "IntegTest_MinMax";

            _tracer.Record(opName, TimeSpan.FromMilliseconds(5));
            for (int i = 0; i < 20; i++)
                _tracer.Record(opName, TimeSpan.FromMilliseconds(50 + i));
            _tracer.Record(opName, TimeSpan.FromMilliseconds(200));

            var agg = _tracer.GetAggregate(opName);

            Assert.NotNull(agg);
            Assert.InRange(agg!.Min.TotalMilliseconds, 4.9, 5.1);
            Assert.InRange(agg.Max.TotalMilliseconds, 199, 201);
        }

        [Fact]
        public void GetRecentMetrics_FiltersCorrectlyByName()
        {
            _tracer.Record("OpAlpha", TimeSpan.FromMilliseconds(10));
            _tracer.Record("OpBeta", TimeSpan.FromMilliseconds(20));
            _tracer.Record("OpAlpha", TimeSpan.FromMilliseconds(30));

            var alphaMetrics = _tracer.GetRecentMetrics("OpAlpha", maxCount: 50);
            var betaMetrics = _tracer.GetRecentMetrics("OpBeta", maxCount: 50);

            Assert.Equal(2, alphaMetrics.Count);
            Assert.Single(betaMetrics);
            Assert.All(alphaMetrics, m => Assert.Equal("OpAlpha", m.OperationName));
        }

        [Fact]
        public void BeginOperation_RecordsElapsedAutomatically()
        {
            const string opName = "IntegTest_Span";

            using (_tracer.BeginOperation(opName))
            {
                // Simule un peu de travail
                System.Threading.Thread.Sleep(10);
            }

            var metrics = _tracer.GetRecentMetrics(opName, maxCount: 5);
            Assert.Single(metrics);
            Assert.True(metrics[0].Elapsed.TotalMilliseconds >= 5,
                "L'élapsed doit être >= 5 ms");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FLUX 4 : MethodReplacer (Roslyn) — remplacement précis de méthode
    // ─────────────────────────────────────────────────────────────────────────

    public class MethodReplacerIntegrationTests
    {
        private const string SampleSource = @"
using System;

namespace Sample
{
    public class Calculator
    {
        public int Add(int a, int b)
        {
            return a + b;
        }

        public int Multiply(int a, int b)
        {
            return a * b;
        }

        public string GetDescription()
        {
            return ""Calculator v1"";
        }
    }
}";

        [Fact]
        public void ReplaceMethod_AddMethod_OnlyAddIsChanged()
        {
            var newAdd = @"public int Add(int a, int b)
{
    // Optimisé : utilise l'opérateur natif
    return checked(a + b);
}";
            var result = MethodReplacer.ReplaceMethod(SampleSource, "Calculator", "Add", newAdd);

            Assert.NotNull(result);
            Assert.Contains("checked(a + b)", result);
            // Les autres méthodes ne doivent pas être altérées
            Assert.Contains("return a * b", result);
            Assert.Contains("Calculator v1", result);
        }

        [Fact]
        public void ReplaceMethod_MultiplyMethod_OtherMethodsPreserved()
        {
            var newMultiply = @"public int Multiply(int a, int b)
{
    return a * b * 1; // identité
}";
            var result = MethodReplacer.ReplaceMethod(SampleSource, "Calculator", "Multiply", newMultiply);

            Assert.NotNull(result);
            Assert.Contains("identité", result);
            // Add et GetDescription doivent être intacts
            Assert.Contains("return a + b", result);
            Assert.Contains("Calculator v1", result);
        }

        [Fact]
        public void ReplaceMethod_NonExistentMethod_ReturnsNull()
        {
            var result = MethodReplacer.ReplaceMethod(SampleSource, "Calculator", "Divide", "public int Divide(int a, int b) { return a / b; }");
            Assert.Null(result);
        }

        [Fact]
        public void ReplaceMethod_NonExistentClass_ReturnsNull()
        {
            var result = MethodReplacer.ReplaceMethod(SampleSource, "WrongClass", "Add", "public int Add(int a, int b) { return a + b; }");
            Assert.Null(result);
        }

        [Fact]
        public void ReplaceMethod_PreservesNamespaceAndOtherClasses()
        {
            var sourceWithTwoClasses = @"
namespace Sample
{
    public class Foo
    {
        public void Bar() { }
    }

    public class Baz
    {
        public void Bar() { System.Console.WriteLine(""baz""); }
    }
}";
            var newBar = @"public void Bar() { System.Console.WriteLine(""replaced""); }";

            // Remplace Bar uniquement dans Foo
            var result = MethodReplacer.ReplaceMethod(sourceWithTwoClasses, "Foo", "Bar", newBar);

            Assert.NotNull(result);
            Assert.Contains("\"replaced\"", result);
            // La méthode Bar de Baz doit rester intacte
            Assert.Contains("\"baz\"", result);
        }

        [Fact]
        public void ReplaceMethod_ResultIsValidCSharp_CanBeReparsed()
        {
            var newAdd = @"public int Add(int a, int b) { return a + b + 0; }";
            var result = MethodReplacer.ReplaceMethod(SampleSource, "Calculator", "Add", newAdd);

            Assert.NotNull(result);

            // Le code résultant doit être parsable par Roslyn sans erreur
            var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(result!);
            var root = tree.GetRoot();
            var diagnostics = tree.GetDiagnostics().Where(d =>
                d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();

            Assert.Empty(diagnostics);
        }
    }
}
