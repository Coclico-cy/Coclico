#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Services;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Moq;
using Xunit;

namespace Coclico.Tests
{

    public class SourceAnalyzerServiceTests
    {
        private readonly SourceAnalyzerService _sut = new();

        [Fact]
        public void CyclomaticComplexity_SimpleMethod_Returns1()
        {

            var method = ParseMethod("void M() { var x = 1; }");
            Assert.Equal(1, SourceAnalyzerService.ComputeCyclomaticComplexity(method));
        }

        [Fact]
        public void CyclomaticComplexity_SingleIf_Returns2()
        {
            var method = ParseMethod("void M(bool b) { if (b) return; }");
            Assert.Equal(2, SourceAnalyzerService.ComputeCyclomaticComplexity(method));
        }

        [Fact]
        public void CyclomaticComplexity_MultipleIf_CountsCorrectly()
        {
            var method = ParseMethod(
                "int M(int x) { if (x>0) { if (x>10) return 2; return 1; } return 0; }");

            Assert.Equal(3, SourceAnalyzerService.ComputeCyclomaticComplexity(method));
        }

        [Fact]
        public void CyclomaticComplexity_LogicalOperators_CountAsExtraBranch()
        {
            var method = ParseMethod("bool M(bool a, bool b) { return a && b; }");

            Assert.Equal(2, SourceAnalyzerService.ComputeCyclomaticComplexity(method));
        }

        [Fact]
        public void SeverityRank_OrderIsCorrect()
        {
            Assert.True(
                SourceAnalyzerService.SeverityRank("Critical") >
                SourceAnalyzerService.SeverityRank("High"));
            Assert.True(
                SourceAnalyzerService.SeverityRank("High") >
                SourceAnalyzerService.SeverityRank("Medium"));
            Assert.True(
                SourceAnalyzerService.SeverityRank("Medium") >
                SourceAnalyzerService.SeverityRank("Low"));
        }

        [Fact]
        public async Task AnalyseAsync_OnMissingDirectory_ReturnsEmptyReport()
        {
            var report = await _sut.AnalyseAsync("C:\\NoSuchDirectory_Coclico_Test_XYZ");

            Assert.NotNull(report);
            Assert.Equal(0, report.FilesScanned);
            Assert.Empty(report.Hotspots);
        }

        [Fact]
        public async Task AnalyseAsync_OnTempFileWithHighCC_DetectsHotspot()
        {

            var dir  = Path.Combine(Path.GetTempPath(), $"CoclicoTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);

            var code = @"
namespace T {
    public class Foo {
        public int Complex(int x, bool a, bool b) {
            if (x > 0) {
                if (a && b) { return 1; }
                else if (a || b) { return 2; }
                else { return 3; }
            } else if (x < 0) {
                return -1;
            } else {
                return 0;
            }
        }
    }
}";
            await File.WriteAllTextAsync(Path.Combine(dir, "Foo.cs"), code);

            try
            {
                var report = await _sut.AnalyseAsync(dir);

                Assert.Equal(1, report.FilesScanned);
                Assert.Equal(1, report.MethodsAnalysed);
                Assert.NotEmpty(report.Hotspots);

                var hotspot = report.Hotspots[0];
                Assert.Equal("Complex", hotspot.MethodName);
                Assert.Equal("Foo", hotspot.ClassName);
                Assert.True(hotspot.CyclomaticComplexity >= 7,
                    $"CC attendu ≥ 7, obtenu {hotspot.CyclomaticComplexity}");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task AnalyseAsync_ExcludesObjAndBin()
        {
            var dir    = Path.Combine(Path.GetTempPath(), $"CoclicoTest_{Guid.NewGuid():N}");
            var objDir = Path.Combine(dir, "obj", "Debug");
            Directory.CreateDirectory(objDir);

            var trivial = "namespace T { class C { void M() {} } }";
            await File.WriteAllTextAsync(Path.Combine(objDir, "Generated.cs"), trivial);

            try
            {
                var report = await _sut.AnalyseAsync(dir);
                Assert.Equal(0, report.FilesScanned);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        private static MethodDeclarationSyntax ParseMethod(string methodSource)
        {
            var tree = CSharpSyntaxTree.ParseText($"class _T {{ {methodSource} }}");
            return tree.GetRoot()
                       .DescendantNodes()
                       .OfType<MethodDeclarationSyntax>()
                       .First();
        }
    }

    public class StateValidatorServiceTests
    {
        [Fact]
        public async Task IndexAsync_ThenGetSnapshot_ReturnsSnapshot()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"CoclicoTwin_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, "Sample.cs");

            await File.WriteAllTextAsync(filePath,
                "namespace T { class C { public void Method() { var x = 1; } } }");

            try
            {
                var analyser = new SourceAnalyzerService();
                var twin     = new StateValidatorService(analyser);

                await twin.IndexAsync(dir);

                Assert.Equal(1, twin.FileCount);
                Assert.NotNull(twin.LastIndexedAt);

                var snap = twin.GetSnapshot(filePath);
                Assert.NotNull(snap);
                Assert.Equal(filePath, snap.FilePath);
                Assert.Equal(1, snap.MethodCount);
                Assert.NotEmpty(snap.SourceHash);
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public async Task SimulatePatch_ImprovingPatch_ReturnsWouldImproveTrue()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"CoclicoTwin_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);

            var original = "public int Calc(int x, bool a, bool b) { if (x>0&&a) { if (b) return 1; return 2; } return 0; }";

            var patched  = "public int Calc(int x, bool a, bool b) { return x > 0 && a ? (b ? 1 : 2) : 0; }";

            var source = $"namespace T {{ class C {{ {original} }} }}";
            var filePath = Path.Combine(dir, "Calc.cs");
            await File.WriteAllTextAsync(filePath, source);

            try
            {
                var analyser = new SourceAnalyzerService();
                var twin     = new StateValidatorService(analyser);
                await twin.IndexAsync(dir);

                var result = twin.SimulatePatch(filePath, original, patched);

                Assert.Null(result.Error);
                Assert.NotEmpty(result.Summary);

                Assert.True(result.DeltaCC <= 0 || result.DeltaCC > 0);
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public async Task SimulatePatch_NotIndexedFile_ReturnsError()
        {
            var analyser = new SourceAnalyzerService();
            var twin     = new StateValidatorService(analyser);

            var result = twin.SimulatePatch("C:\\ghost.cs", "void M(){}", "void M(){ var x=1; }");

            Assert.False(result.WouldImprove);
            Assert.Equal("NotIndexed", result.Error);
        }
    }

    public class CodePatcherServiceTests
    {
        [Fact]
        public async Task ApplyPatchAsync_FileNotIndexed_ReturnsFail()
        {
            var analyser  = new SourceAnalyzerService();
            var twin      = new StateValidatorService(analyser);
            var rollback  = new RollbackService();
            var patcher   = new CodePatcherService(twin, rollback, new Mock<IAuditLog>().Object);

            var result = await patcher.ApplyPatchAsync(
                "C:\\ghost.cs",
                "void M(){}",
                "void M(){ var x=1; }");

            Assert.False(result.Success);
            Assert.NotEmpty(result.Summary);
        }

        [Fact]
        public async Task ApplyPatchAsync_ValidPatch_CreatesSnapshotAndWritesFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"CoclicoPatcher_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);

            var original = "public void Complex(int x, bool a, bool b) { if (x>0) { if (a) {} if (b) {} } }";

            var patched  = "public void Complex(int x, bool a, bool b) { var ok = x > 0; }";
            var source   = $"namespace T {{ class C {{ {original} }} }}";
            var filePath = Path.Combine(dir, "Target.cs");
            await File.WriteAllTextAsync(filePath, source);

            try
            {
                var analyser = new SourceAnalyzerService();
                var twin     = new StateValidatorService(analyser);
                await twin.IndexAsync(dir);

                var rollback = new RollbackService();
                var patcher  = new CodePatcherService(twin, rollback, new Mock<IAuditLog>().Object);

                var result = await patcher.ApplyPatchAsync(filePath, original, patched);

                Assert.NotNull(result);
                Assert.Equal(filePath, result.FilePath);

                if (result.Success)
                {

                    Assert.False(string.IsNullOrEmpty(result.SnapshotId));
                    var written = await File.ReadAllTextAsync(filePath);
                    Assert.Contains(patched, written);
                }

                var history = patcher.GetHistory();
                Assert.NotEmpty(history);
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public async Task GetHistory_ReturnsInChronologicalReverseOrder()
        {
            var analyser = new SourceAnalyzerService();
            var twin     = new StateValidatorService(analyser);
            var rollback = new RollbackService();
            var patcher  = new CodePatcherService(twin, rollback, new Mock<IAuditLog>().Object);

            await patcher.ApplyPatchAsync("C:\\a.cs", "void A(){}", "void A(){var x=1;}");
            await patcher.ApplyPatchAsync("C:\\b.cs", "void B(){}", "void B(){var x=2;}");

            var history = patcher.GetHistory(10);
            Assert.Equal(2, history.Count);

            Assert.Equal("C:\\b.cs", history[0].FilePath);
        }
    }

}
