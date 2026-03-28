#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Coclico.Services;

public sealed record CodePatchResult(
    bool Success,
    string FilePath,
    string MethodName,
    int DeltaCC,
    int DeltaLines,
    string SnapshotId,
    string Summary,
    DateTimeOffset AppliedAt,
    string? Error = null,
    bool IsAuditProposal = false,
    double SimulationScore = 0.0,
    string? OriginalSource = null,
    string? PatchedSource = null);

public interface ICodePatcher
{
    Task<CodePatchResult> ApplyPatchAsync(
        string filePath,
        string originalMethodSource,
        string patchedMethodSource,
        CancellationToken ct = default);

    IReadOnlyList<CodePatchResult> GetHistory(int max = 20);

    IReadOnlyList<CodePatchResult> GetPendingProposals();

    Task<CodePatchResult> ApproveAndApplyAsync(
        CodePatchResult proposal,
        CancellationToken ct = default);

    Task RejectProposalAsync(CodePatchResult proposal, string reason = "Manuel");
}
