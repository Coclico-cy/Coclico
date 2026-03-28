#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Coclico.Services;

public sealed record CodeHotspot(
    string FilePath,
    string MethodName,
    string ClassName,
    int LineNumber,
    int LineCount,
    int CyclomaticComplexity,
    string Severity,
    int CognitiveComplexity = 0,
    double HalsteadVolume = 0.0,
    double HalsteadDifficulty = 0.0,
    double HalsteadEffort = 0.0,
    double MaintainabilityIndex = 100.0);

public sealed record SourceAnalysisReport(
    DateTimeOffset AnalysedAt,
    int FilesScanned,
    int MethodsAnalysed,
    IReadOnlyList<CodeHotspot> Hotspots,
    IReadOnlyList<string> Log);

public interface ISourceAnalyzer
{
    Task<SourceAnalysisReport> AnalyseAsync(string sourceRoot, CancellationToken ct = default);

    SourceAnalysisReport? LastReport { get; }
}
