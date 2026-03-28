#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Coclico.Services;

public sealed class StateValidatorService(ISourceAnalyzer analyser) : IStateValidator
{
    private readonly ISourceAnalyzer _analyser = analyser;
    private readonly ConcurrentDictionary<string, (CodeFileSnapshot Snapshot, string RawSource)> _index = new();
    private readonly ConcurrentDictionary<string, string> _fileHashes = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset? LastIndexedAt { get; private set; }
    public int FileCount => _index.Count;

    public async Task IndexAsync(string sourceRoot, CancellationToken ct = default)
    {
        LoggingService.LogInfo($"[StateValidator] Indexage de {sourceRoot}…");

        var report = await _analyser.AnalyseAsync(sourceRoot, ct).ConfigureAwait(false);

        var byFile = report.Hotspots
            .GroupBy(h => h.FilePath)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CodeHotspot>)g.ToList());

        var csFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                               .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        int parsed = 0, skipped = 0;
        var currentFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in csFiles)
        {
            ct.ThrowIfCancellationRequested();
            currentFiles.Add(file);
            try
            {
                var raw = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                var hash = ComputeHash(raw);

                if (_fileHashes.TryGetValue(file, out var cachedHash) && cachedHash == hash)
                {
                    skipped++;
                    continue;
                }

                var hotspots = byFile.TryGetValue(file, out var h) ? h : Array.Empty<CodeHotspot>();
                var methods = CountMethods(raw);

                var snapshot = new CodeFileSnapshot(
                    FilePath: file,
                    SourceHash: hash,
                    IndexedAt: DateTimeOffset.UtcNow,
                    MethodCount: methods,
                    Hotspots: hotspots);

                _index[file] = (snapshot, raw);
                _fileHashes[file] = hash;
                parsed++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, $"StateValidator.Index({Path.GetFileName(file)})");
            }
        }

        foreach (var stale in _index.Keys)
        {
            if (!currentFiles.Contains(stale))
            {
                _index.TryRemove(stale, out _);
                _fileHashes.TryRemove(stale, out _);
            }
        }

        LastIndexedAt = DateTimeOffset.UtcNow;
        LoggingService.LogInfo(
            $"[StateValidator] Clone prêt — {_index.Count} fichiers (reparsés={parsed}, skippés={skipped}).");
    }

    public IReadOnlyList<CodeFileSnapshot> GetSnapshots() =>
        _index.Values
              .Select(e => e.Snapshot)
              .OrderByDescending(s => s.Hotspots.Count)
              .ToList();

    public CodeFileSnapshot? GetSnapshot(string filePath) =>
        _index.TryGetValue(filePath, out var e) ? e.Snapshot : null;

    private const double ImprovementThreshold = 0.05;

    public PatchSimulationResult SimulatePatch(
        string filePath,
        string originalMethodSource,
        string patchedMethodSource)
    {
        LoggingService.LogInfo($"[StateValidatorService.SimulatePatch] Entry — filePath={filePath}");
        try
        {
            if (!_index.TryGetValue(filePath, out var entry))
                return new PatchSimulationResult(
                    false, 0, 0,
                    "Fichier non indexé dans le Clone Numérique.",
                    "NotIndexed");

            if (!entry.RawSource.Contains(originalMethodSource, StringComparison.Ordinal))
                return new PatchSimulationResult(
                    false, 0, 0,
                    "Méthode originale introuvable dans le fichier indexé.",
                    "OriginalNotFound");

            int ccOriginal = ComputeMethodCC(originalMethodSource);
            int ccPatched = ComputeMethodCC(patchedMethodSource);
            int linesOriginal = CountLines(originalMethodSource);
            int linesPatched = CountLines(patchedMethodSource);

            int deltaCC = ccPatched - ccOriginal;
            int deltaLines = linesPatched - linesOriginal;

            var (volOrig, diffOrig, effOrig) = ComputeHalstead(originalMethodSource);
            var (volPatch, diffPatch, effPatch) = ComputeHalstead(patchedMethodSource);

            double deltaVolume = volPatch - volOrig;
            double deltaDifficulty = diffPatch - diffOrig;
            double deltaEffort = effPatch - effOrig;

            double normCC = -(double)deltaCC / (ccOriginal + 1);
            double normLines = -(double)deltaLines / (linesOriginal + 1);
            double normVol = -deltaVolume / (volOrig + 1);
            double normDiff = -deltaDifficulty / (diffOrig + 1);
            double normEffort = -deltaEffort / (effOrig + 1);

            double score = 0.35 * normCC
                         + 0.15 * normLines
                         + 0.20 * normVol
                         + 0.15 * normDiff
                         + 0.15 * normEffort;

            bool improves = score > ImprovementThreshold;

            var summary = improves
                ? $"Clone valide : score={score:+0.000;-0.000}, ΔCC={deltaCC:+0;-0}, ΔLignes={deltaLines:+0;-0}, ΔVol={deltaVolume:+0.0;-0.0}, ΔEff={deltaEffort:+0.0;-0.0} — amélioration confirmée."
                : $"Clone rejette : score={score:+0.000;-0.000}, ΔCC={deltaCC:+0;-0}, ΔLignes={deltaLines:+0;-0}, ΔVol={deltaVolume:+0.0;-0.0}, ΔEff={deltaEffort:+0.0;-0.0} — régression détectée.";

            LoggingService.LogInfo(
                $"[StateValidator] Patch simulé sur {Path.GetFileName(filePath)} → {summary}");

            LoggingService.LogInfo($"[StateValidatorService.SimulatePatch] Exit — result=improves={improves}, score={score:F3}");
            return new PatchSimulationResult(
                improves, deltaCC, deltaLines, summary,
                Score: score,
                DeltaVolume: deltaVolume,
                DeltaDifficulty: deltaDifficulty,
                DeltaEffort: deltaEffort);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "StateValidator.SimulatePatch");
            return new PatchSimulationResult(
                false, 0, 0, "Erreur durant la simulation.", ex.Message);
        }
    }

    private static int ComputeMethodCC(string methodSource)
    {
        try
        {
            var wrapper = $"class _T {{ {methodSource} }}";
            var tree = CSharpSyntaxTree.ParseText(wrapper);
            var method = tree.GetRoot()
                              .DescendantNodes()
                              .OfType<MethodDeclarationSyntax>()
                              .FirstOrDefault();

            if (method is null) return 1;

            return SourceAnalyzerService.ComputeCyclomaticComplexity(method);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "StateValidatorService.ComputeMethodCC");
            return 1;
        }
    }

    private static int CountMethods(string source)
    {
        try
        {
            return CSharpSyntaxTree.ParseText(source)
                                   .GetRoot()
                                   .DescendantNodes()
                                   .OfType<MethodDeclarationSyntax>()
                                   .Count();
        }
        catch (Exception ex) { LoggingService.LogException(ex, "StateValidatorService.CountMethods"); return 0; }
    }

    private static int CountLines(string source) =>
        source.Split('\n').Length;

    private static (double Volume, double Difficulty, double Effort) ComputeHalstead(string methodSource)
    {
        try
        {
            var wrapper = $"class _T {{ {methodSource} }}";
            var tree = CSharpSyntaxTree.ParseText(wrapper);
            var root = tree.GetRoot();

            var operatorKinds = new HashSet<SyntaxKind>
            {
                SyntaxKind.PlusToken, SyntaxKind.MinusToken, SyntaxKind.AsteriskToken,
                SyntaxKind.SlashToken, SyntaxKind.PercentToken, SyntaxKind.AmpersandToken,
                SyntaxKind.BarToken, SyntaxKind.CaretToken, SyntaxKind.TildeToken,
                SyntaxKind.ExclamationToken, SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken,
                SyntaxKind.EqualsToken, SyntaxKind.PlusEqualsToken, SyntaxKind.MinusEqualsToken,
                SyntaxKind.AsteriskEqualsToken, SyntaxKind.SlashEqualsToken, SyntaxKind.PercentEqualsToken,
                SyntaxKind.AmpersandAmpersandToken, SyntaxKind.BarBarToken, SyntaxKind.EqualsEqualsToken,
                SyntaxKind.ExclamationEqualsToken, SyntaxKind.LessThanEqualsToken, SyntaxKind.GreaterThanEqualsToken,
                SyntaxKind.IfKeyword, SyntaxKind.ElseKeyword, SyntaxKind.ForKeyword,
                SyntaxKind.ForEachKeyword, SyntaxKind.WhileKeyword, SyntaxKind.DoKeyword,
                SyntaxKind.ReturnKeyword, SyntaxKind.NewKeyword, SyntaxKind.ThisKeyword,
                SyntaxKind.DotToken, SyntaxKind.ColonToken, SyntaxKind.QuestionToken,
            };

            var allTokens = root.DescendantTokens().ToList();

            int n1 = 0;
            int n2 = 0;
            var distinct1 = new HashSet<string>();
            var distinct2 = new HashSet<string>();

            foreach (var tok in allTokens)
            {
                if (operatorKinds.Contains(tok.Kind()))
                {
                    n1++;
                    distinct1.Add(tok.Text);
                }
#pragma warning disable RS1034
                else if (tok.Kind() == SyntaxKind.IdentifierToken ||
                         tok.Kind() == SyntaxKind.NumericLiteralToken ||
                         tok.Kind() == SyntaxKind.StringLiteralToken ||
                         tok.Kind() == SyntaxKind.CharacterLiteralToken)
#pragma warning restore RS1034
                {
                    n2++;
                    distinct2.Add(tok.Text);
                }
            }

            int eta1 = Math.Max(1, distinct1.Count);
            int eta2 = Math.Max(1, distinct2.Count);
            int N = n1 + n2;
            int eta = eta1 + eta2;

            double volume = N * Math.Log2(Math.Max(2, eta));
            double difficulty = (eta1 / 2.0) * ((double)n2 / eta2);
            double effort = difficulty * volume;

            return (volume, difficulty, effort);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "StateValidatorService.ComputeHalstead");
            return (0, 0, 0);
        }
    }

    private static string ComputeHash(string source)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes)[..16];
    }
}
