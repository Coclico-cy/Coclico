#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Coclico.Services;

public sealed class SourceAnalyzerService : ISourceAnalyzer
{
    private const int CcMedium = 6;
    private const int CcHigh = 11;
    private const int CcCritical = 21;
    private const int LineLarge = 80;

    public SourceAnalysisReport? LastReport { get; private set; }

    public async Task<SourceAnalysisReport> AnalyseAsync(
        string sourceRoot,
        CancellationToken ct = default)
    {
        LoggingService.LogInfo($"[SourceAnalyzerService.AnalyseAsync] Entry — sourceRoot={sourceRoot}");
        var start = DateTimeOffset.UtcNow;
        var log = new List<string>();
        var hotspots = new List<CodeHotspot>();
        int files = 0, methods = 0;

        if (!Directory.Exists(sourceRoot))
        {
            log.Add($"[SourceAnalyzer] Répertoire introuvable : {sourceRoot}");
            LoggingService.LogInfo($"[SourceAnalyzerService.AnalyseAsync] Exit — result=DirectoryNotFound");
            return Finalise(start, files, methods, hotspots, log);
        }

        var csFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                               .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                               .ToList();

        log.Add($"[SourceAnalyzer] {csFiles.Count} fichiers .cs trouvés dans {sourceRoot}");

        foreach (var filePath in csFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var source = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
                var tree = CSharpSyntaxTree.ParseText(source);
                var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

                files++;
                var fileHotspots = AnalyseFile(filePath, root, ref methods);
                hotspots.AddRange(fileHotspots);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log.Add($"[SourceAnalyzer] Erreur sur {Path.GetFileName(filePath)} : {ex.Message}");
            }
        }

        hotspots.Sort((a, b) =>
        {
            int cmp = SeverityRank(b.Severity).CompareTo(SeverityRank(a.Severity));
            return cmp != 0 ? cmp : b.CyclomaticComplexity.CompareTo(a.CyclomaticComplexity);
        });

        log.Add($"[SourceAnalyzer] Analyse terminée — {files} fichiers, {methods} méthodes, {hotspots.Count} hotspots.");

        if (hotspots.Count > 0)
        {
            var worst = hotspots[0];
            log.Add($"[SourceAnalyzer] Pire hotspot : {worst.ClassName}.{worst.MethodName} " +
                    $"(CC={worst.CyclomaticComplexity}, {worst.LineCount} lignes, {worst.Severity})");
        }

        var report = Finalise(start, files, methods, hotspots, log);
        LoggingService.LogInfo($"[SourceAnalyzerService.AnalyseAsync] Exit — result=files={files}, methods={methods}, hotspots={hotspots.Count}");
        return report;
    }

    private static List<CodeHotspot> AnalyseFile(
        string filePath,
        SyntaxNode root,
        ref int methodCount)
    {
        var result = new List<CodeHotspot>();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            methodCount++;

            var lineSpan = method.GetLocation().GetLineSpan();
            var lineStart = lineSpan.StartLinePosition.Line + 1;
            var lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;
            var cc = ComputeCyclomaticComplexity(method);
            var severity = ComputeSeverity(cc, lineCount);

            if (severity == "Low" && lineCount <= 20) continue;

            var cogCC = ComputeCognitiveComplexity(method);
            var (hVol, hDiff, hEff) = ComputeHalstead(method);
            var mi = ComputeMaintainabilityIndex(hVol, cc, lineCount);

            result.Add(new CodeHotspot(
                FilePath: filePath,
                MethodName: method.Identifier.Text,
                ClassName: GetContainingClassName(method),
                LineNumber: lineStart,
                LineCount: lineCount,
                CyclomaticComplexity: cc,
                Severity: severity,
                CognitiveComplexity: cogCC,
                HalsteadVolume: hVol,
                HalsteadDifficulty: hDiff,
                HalsteadEffort: hEff,
                MaintainabilityIndex: mi));
        }

        return result;
    }

    public static int ComputeCyclomaticComplexity(MethodDeclarationSyntax method)
    {
        int cc = 1;

        foreach (var node in method.DescendantNodes())
        {
            cc += node switch
            {
                IfStatementSyntax => 1,
                WhileStatementSyntax => 1,
                ForStatementSyntax => 1,
                ForEachStatementSyntax => 1,
                DoStatementSyntax => 1,
                CaseSwitchLabelSyntax => 1,
                SwitchExpressionArmSyntax => 1,
                CatchClauseSyntax => 1,
                ConditionalExpressionSyntax => 1,
                BinaryExpressionSyntax bin
                    when bin.IsKind(SyntaxKind.LogicalAndExpression)
                      || bin.IsKind(SyntaxKind.LogicalOrExpression) => 1,
                _ => 0,
            };
        }

        return cc;
    }

    private static readonly HashSet<SyntaxKind> _operatorTokenKinds = new()
    {
        SyntaxKind.PlusToken, SyntaxKind.MinusToken, SyntaxKind.AsteriskToken,
        SyntaxKind.SlashToken, SyntaxKind.PercentToken, SyntaxKind.AmpersandToken,
        SyntaxKind.BarToken, SyntaxKind.CaretToken, SyntaxKind.TildeToken,
        SyntaxKind.ExclamationToken, SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken,
        SyntaxKind.EqualsToken, SyntaxKind.PlusEqualsToken, SyntaxKind.MinusEqualsToken,
        SyntaxKind.AsteriskEqualsToken, SyntaxKind.SlashEqualsToken, SyntaxKind.PercentEqualsToken,
        SyntaxKind.AmpersandAmpersandToken, SyntaxKind.BarBarToken, SyntaxKind.EqualsEqualsToken,
        SyntaxKind.ExclamationEqualsToken, SyntaxKind.LessThanEqualsToken, SyntaxKind.GreaterThanEqualsToken,
        SyntaxKind.QuestionQuestionToken, SyntaxKind.QuestionQuestionEqualsToken,
        SyntaxKind.DotToken, SyntaxKind.ColonToken, SyntaxKind.QuestionToken,
        SyntaxKind.EqualsGreaterThanToken,
        SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken,
        SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken,
        SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken,
    };

    private static readonly HashSet<SyntaxKind> _flowKeywordKinds = new()
    {
        SyntaxKind.IfKeyword, SyntaxKind.ElseKeyword,
        SyntaxKind.ForKeyword, SyntaxKind.ForEachKeyword,
        SyntaxKind.WhileKeyword, SyntaxKind.DoKeyword,
        SyntaxKind.SwitchKeyword, SyntaxKind.CaseKeyword,
        SyntaxKind.ReturnKeyword, SyntaxKind.BreakKeyword,
        SyntaxKind.ContinueKeyword, SyntaxKind.ThrowKeyword,
        SyntaxKind.CatchKeyword, SyntaxKind.FinallyKeyword,
    };

    public static (double Volume, double Difficulty, double Effort) ComputeHalstead(
        MethodDeclarationSyntax method)
    {
        try
        {
            int n1 = 0, n2 = 0;
            var distinct1 = new HashSet<string>();
            var distinct2 = new HashSet<string>();

            foreach (var tok in method.DescendantTokens())
            {
                var kind = tok.Kind();
                if (_operatorTokenKinds.Contains(kind) || _flowKeywordKinds.Contains(kind))
                {
                    n1++;
                    distinct1.Add(tok.Text);
                }
                else if (kind is SyntaxKind.IdentifierToken
                              or SyntaxKind.NumericLiteralToken
                              or SyntaxKind.StringLiteralToken
                              or SyntaxKind.CharacterLiteralToken
                              or SyntaxKind.TrueKeyword
                              or SyntaxKind.FalseKeyword
                              or SyntaxKind.NullKeyword)
                {
                    n2++;
                    distinct2.Add(tok.Text);
                }
            }

            int eta1 = Math.Max(1, distinct1.Count);
            int eta2 = Math.Max(1, distinct2.Count);
            int N = n1 + n2;
            int eta = eta1 + eta2;
            double vol = N * Math.Log2(Math.Max(2, eta));
            double dif = (eta1 / 2.0) * ((double)n2 / eta2);
            double eff = dif * vol;

            return (vol, dif, eff);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "SourceAnalyzerService.ComputeHalstead");
            return (0.0, 0.0, 0.0);
        }
    }

    public static double ComputeMaintainabilityIndex(double halsteadVolume, int cc, int lineCount)
    {
        double lnV = halsteadVolume > 0 ? Math.Log(halsteadVolume) : 0.0;
        double lnLoc = lineCount > 0 ? Math.Log(Math.Max(1, lineCount)) : 0.0;
        double raw = 171.0 - 5.2 * lnV - 0.23 * cc - 16.2 * lnLoc;
        return Math.Max(0.0, Math.Min(100.0, raw / 171.0 * 100.0));
    }

    public static int ComputeCognitiveComplexity(MethodDeclarationSyntax method)
    {
        var walker = new CognitiveComplexityWalker();
        walker.Visit(method);
        return walker.Score;
    }

    private sealed class CognitiveComplexityWalker : CSharpSyntaxWalker
    {
        public int Score { get; private set; }
        private int _nesting;

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            bool isElseIf = node.Parent is ElseClauseSyntax;
            Score += 1 + (isElseIf ? 0 : _nesting);
            _nesting++;
            base.VisitIfStatement(node);
            _nesting--;
        }

        public override void VisitElseClause(ElseClauseSyntax node)
        {
            if (node.Statement is not IfStatementSyntax)
                Score += 1;
            base.VisitElseClause(node);
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            Score += 1 + _nesting; _nesting++;
            base.VisitForStatement(node);
            _nesting--;
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            Score += 1 + _nesting; _nesting++;
            base.VisitForEachStatement(node);
            _nesting--;
        }

        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            Score += 1 + _nesting; _nesting++;
            base.VisitWhileStatement(node);
            _nesting--;
        }

        public override void VisitDoStatement(DoStatementSyntax node)
        {
            Score += 1 + _nesting; _nesting++;
            base.VisitDoStatement(node);
            _nesting--;
        }

        public override void VisitSwitchStatement(SwitchStatementSyntax node)
        {
            Score += 1 + _nesting; _nesting++;
            base.VisitSwitchStatement(node);
            _nesting--;
        }

        public override void VisitSwitchExpression(SwitchExpressionSyntax node)
        {
            Score += 1 + _nesting; _nesting++;
            base.VisitSwitchExpression(node);
            _nesting--;
        }

        public override void VisitCatchClause(CatchClauseSyntax node)
        {
            Score += 1 + _nesting; _nesting++;
            base.VisitCatchClause(node);
            _nesting--;
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            Score += 1 + _nesting;
            base.VisitConditionalExpression(node);
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            var kind = node.Kind();
            if (kind is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
            {
                var parentKind = node.Parent?.Kind() ?? SyntaxKind.None;
                bool parentIsLogical = parentKind is SyntaxKind.LogicalAndExpression
                                                  or SyntaxKind.LogicalOrExpression;
                bool sameOpAsParent = parentIsLogical && parentKind == kind;

                if (!sameOpAsParent)
                    Score++;
            }
            base.VisitBinaryExpression(node);
        }

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            _nesting++; base.VisitSimpleLambdaExpression(node); _nesting--;
        }

        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            _nesting++; base.VisitParenthesizedLambdaExpression(node); _nesting--;
        }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            _nesting++; base.VisitLocalFunctionStatement(node); _nesting--;
        }
    }

    private static string ComputeSeverity(int cc, int lineCount)
    {
        var sev = cc switch
        {
            >= CcCritical => "Critical",
            >= CcHigh => "High",
            >= CcMedium => "Medium",
            _ => "Low",
        };

        if (lineCount > LineLarge && sev != "Critical")
        {
            sev = sev switch
            {
                "Low" => "Medium",
                "Medium" => "High",
                "High" => "Critical",
                _ => sev,
            };
        }

        return sev;
    }

    public static int SeverityRank(string sev) => sev switch
    {
        "Critical" => 4,
        "High" => 3,
        "Medium" => 2,
        _ => 1,
    };

    private static string GetContainingClassName(MethodDeclarationSyntax method)
    {
        var parent = method.Parent;
        while (parent is not null)
        {
            if (parent is ClassDeclarationSyntax cls) return cls.Identifier.Text;
            if (parent is StructDeclarationSyntax str) return str.Identifier.Text;
            if (parent is RecordDeclarationSyntax rec) return rec.Identifier.Text;
            parent = parent.Parent;
        }
        return "<unknown>";
    }

    private SourceAnalysisReport Finalise(
        DateTimeOffset start,
        int files,
        int methods,
        List<CodeHotspot> hotspots,
        List<string> log)
    {
        LastReport = new SourceAnalysisReport(
            AnalysedAt: start,
            FilesScanned: files,
            MethodsAnalysed: methods,
            Hotspots: hotspots,
            Log: log);

        LoggingService.LogInfo(
            $"[SourceAnalyzer] {files} fichiers / {methods} méthodes / {hotspots.Count} hotspots");

        return LastReport;
    }
}
