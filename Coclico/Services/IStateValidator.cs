#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Coclico.Services;

public sealed record CodeFileSnapshot(
    string FilePath,
    string SourceHash,
    DateTimeOffset IndexedAt,
    int MethodCount,
    IReadOnlyList<CodeHotspot> Hotspots);

public sealed record PatchSimulationResult(
    bool WouldImprove,
    int DeltaCC,
    int DeltaLines,
    string Summary,
    string? Error = null,
    double Score = 0.0,
    double DeltaVolume = 0.0,
    double DeltaDifficulty = 0.0,
    double DeltaEffort = 0.0);

public interface IStateValidator
{
    Task IndexAsync(string sourceRoot, CancellationToken ct = default);

    IReadOnlyList<CodeFileSnapshot> GetSnapshots();

    CodeFileSnapshot? GetSnapshot(string filePath);

    PatchSimulationResult SimulatePatch(
        string filePath,
        string originalMethodSource,
        string patchedMethodSource);

    DateTimeOffset? LastIndexedAt { get; }

    int FileCount { get; }
}
