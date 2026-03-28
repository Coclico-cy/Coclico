#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Services.Algorithms;

namespace Coclico.Services;

public sealed class CodePatcherService(
    IStateValidator twin,
    IRollbackService rollback,
    IAuditLog audit) : ICodePatcher
{
    private readonly IStateValidator _twin = twin;
    private readonly IRollbackService _rollback = rollback;
    private readonly IAuditLog _audit = audit;

    private readonly List<CodePatchResult> _history = [];
    private readonly List<CodePatchResult> _pending = [];
    private readonly Lock _histLock = new();

    public async Task<CodePatchResult> ApplyPatchAsync(
        string filePath,
        string originalMethodSource,
        string patchedMethodSource,
        CancellationToken ct = default)
    {
        LoggingService.LogInfo($"[CodePatcherService.ApplyPatchAsync] Entry — filePath={filePath}");
        var methodName = ExtractMethodName(patchedMethodSource);

        try
        {
            var sim = _twin.SimulatePatch(filePath, originalMethodSource, patchedMethodSource);

            if (!sim.WouldImprove)
            {
                LoggingService.LogInfo(
                    $"[CodePatcher] Patch refusé par le Clone : {sim.Summary}");
                await _audit.LogAsync(new AuditEntry(
                    Timestamp: DateTimeOffset.UtcNow,
                    Actor: "CodePatcher",
                    Action: "PatchRejected",
                    Target: $"{Path.GetFileName(filePath)}::{methodName}",
                    Success: false,
                    Details: sim.Summary)).ConfigureAwait(false);
                return Fail(filePath, methodName, sim.DeltaCC, sim.DeltaLines,
                            sim.Summary, "CloneRejected");
            }

            bool auditOnly = ServiceContainer.GetRequired<SettingsService>().Settings.CodePatcherAuditOnly;
            if (auditOnly)
            {
                LoggingService.LogInfo(
                    $"[CodePatcher] [AUDIT] Patch validé mais non appliqué (mode Audit) : " +
                    $"{methodName} — score={sim.Score:+0.000;-0.000}");

                await _audit.LogAsync(new AuditEntry(
                    Timestamp: DateTimeOffset.UtcNow,
                    Actor: "CodePatcher",
                    Action: "PatchProposed",
                    Target: $"{Path.GetFileName(filePath)}::{methodName}",
                    Success: true,
                    Details: $"[AuditMode] {sim.Summary}")).ConfigureAwait(false);

                var proposal = new CodePatchResult(
                    Success: false,
                    FilePath: filePath,
                    MethodName: methodName,
                    DeltaCC: sim.DeltaCC,
                    DeltaLines: sim.DeltaLines,
                    SnapshotId: string.Empty,
                    Summary: $"[PROPOSITION] Patch validé, en attente d'approbation. {sim.Summary}",
                    AppliedAt: DateTimeOffset.UtcNow,
                    Error: "AuditMode",
                    IsAuditProposal: true,
                    SimulationScore: sim.Score,
                    OriginalSource: originalMethodSource,
                    PatchedSource: patchedMethodSource);

                AddHistory(proposal);
                AddPending(proposal);
                return proposal;
            }

            ct.ThrowIfCancellationRequested();

            if (!File.Exists(filePath))
                return Fail(filePath, methodName, 0, 0, "Fichier introuvable.", "FileNotFound");

            var currentSource = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);

            if (!currentSource.Contains(originalMethodSource, StringComparison.Ordinal))
                return Fail(filePath, methodName, 0, 0,
                            "Méthode originale introuvable dans le fichier réel (drift détecté).",
                            "SourceDrift");

            var snapshotId = _rollback.CreateSnapshot(
                $"CodePatcher.{Path.GetFileName(filePath)}.{methodName}",
                new { FilePath = filePath, OriginalSource = currentSource, MethodName = methodName });

            if (string.IsNullOrEmpty(snapshotId))
            {
                LoggingService.LogInfo("[CodePatcher] Snapshot Rollback échoué — patch annulé.");
                return Fail(filePath, methodName, sim.DeltaCC, sim.DeltaLines,
                            "Rollback snapshot impossible — patch annulé par sécurité.", "SnapshotFailed");
            }

            ct.ThrowIfCancellationRequested();

            var className = ExtractClassName(filePath);
            var patchedSource = MethodReplacer.ReplaceMethod(
                currentSource, className, methodName, patchedMethodSource);
            if (patchedSource is null)
                return Fail(filePath, methodName, 0, 0,
                    "Méthode introuvable dans l'AST — patching impossible.", "AstNotFound");

            await File.WriteAllTextAsync(filePath, patchedSource, ct).ConfigureAwait(false);

            var result = new CodePatchResult(
                Success: true,
                FilePath: filePath,
                MethodName: methodName,
                DeltaCC: sim.DeltaCC,
                DeltaLines: sim.DeltaLines,
                SnapshotId: snapshotId,
                Summary: $"Patch appliqué. {sim.Summary} Rollback={snapshotId[..8]}…",
                AppliedAt: DateTimeOffset.UtcNow);

            AddHistory(result);
            LoggingService.LogInfo(
                $"[CodePatcher] ✓ Patch appliqué : {methodName} dans {Path.GetFileName(filePath)} " +
                $"[ΔCC={sim.DeltaCC:+0;-0}, ΔLignes={sim.DeltaLines:+0;-0}]");

            await _audit.LogAsync(new AuditEntry(
                Timestamp: DateTimeOffset.UtcNow,
                Actor: "CodePatcher",
                Action: "PatchApplied",
                Target: $"{Path.GetFileName(filePath)}::{methodName}",
                Success: true,
                Details: $"Rollback={snapshotId[..8]}… {sim.Summary}")).ConfigureAwait(false);

            LoggingService.LogInfo($"[CodePatcherService.ApplyPatchAsync] Exit — result=PatchApplied({methodName})");
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CodePatcherService.ApplyPatchAsync");
            return Fail(filePath, methodName, 0, 0, $"Erreur inattendue : {ex.Message}", ex.GetType().Name);
        }
    }

    public IReadOnlyList<CodePatchResult> GetHistory(int max = 20)
    {
        lock (_histLock)
            return _history.Count <= max
                ? [.._history]
                : _history.GetRange(0, max);
    }

    public IReadOnlyList<CodePatchResult> GetPendingProposals()
    {
        lock (_histLock) return [.._pending];
    }

    public async Task<CodePatchResult> ApproveAndApplyAsync(
        CodePatchResult proposal,
        CancellationToken ct = default)
    {
        LoggingService.LogInfo($"[CodePatcherService.ApproveAndApplyAsync] Entry — proposal={proposal.MethodName}@{Path.GetFileName(proposal.FilePath)}");
        if (!proposal.IsAuditProposal ||
            string.IsNullOrEmpty(proposal.OriginalSource) ||
            string.IsNullOrEmpty(proposal.PatchedSource))
        {
            return Fail(proposal.FilePath, proposal.MethodName, 0, 0,
                        "Proposition invalide ou sans sources stockées.", "InvalidProposal");
        }

        LoggingService.LogInfo(
            $"[CodePatcher] Approbation humaine reçue pour {proposal.MethodName} " +
            $"dans {Path.GetFileName(proposal.FilePath)}");

        var result = await ApplyPatchInternalAsync(
            proposal.FilePath,
            proposal.OriginalSource,
            proposal.PatchedSource,
            approvedByHuman: true,
            ct).ConfigureAwait(false);

        RemovePending(proposal);

        await _audit.LogAsync(new AuditEntry(
            Timestamp: DateTimeOffset.UtcNow,
            Actor: "Human",
            Action: result.Success ? "ProposalApproved" : "ProposalApprovalFailed",
            Target: $"{Path.GetFileName(proposal.FilePath)}::{proposal.MethodName}",
            Success: result.Success,
            Details: result.Summary)).ConfigureAwait(false);

        LoggingService.LogInfo($"[CodePatcherService.ApproveAndApplyAsync] Exit — result={result.Success}");
        return result;
    }

    public async Task RejectProposalAsync(CodePatchResult proposal, string reason = "Manuel")
    {
        LoggingService.LogInfo($"[CodePatcherService.RejectProposalAsync] Entry — proposal={proposal.MethodName}@{Path.GetFileName(proposal.FilePath)}");
        RemovePending(proposal);

        await _audit.LogAsync(new AuditEntry(
            Timestamp: DateTimeOffset.UtcNow,
            Actor: "Human",
            Action: "ProposalRejected",
            Target: $"{Path.GetFileName(proposal.FilePath)}::{proposal.MethodName}",
            Success: false,
            Details: $"Raison : {reason}")).ConfigureAwait(false);

        LoggingService.LogInfo(
            $"[CodePatcher] Proposition rejetée : {proposal.MethodName} — {reason}");
        LoggingService.LogInfo($"[CodePatcherService.RejectProposalAsync] Exit — result=Rejected({proposal.MethodName})");
    }

    private async Task<CodePatchResult> ApplyPatchInternalAsync(
        string filePath,
        string originalMethodSource,
        string patchedMethodSource,
        bool approvedByHuman,
        CancellationToken ct)
    {
        LoggingService.LogInfo($"[CodePatcherService.ApplyPatchInternalAsync] Entry — filePath={filePath}, approvedByHuman={approvedByHuman}");
        var methodName = ExtractMethodName(patchedMethodSource);

        var sim = _twin.SimulatePatch(filePath, originalMethodSource, patchedMethodSource);
        if (!sim.WouldImprove)
            return Fail(filePath, methodName, sim.DeltaCC, sim.DeltaLines,
                        $"Rejet Clone (score={sim.Score:+0.000;-0.000}) — regression détectée.", "CloneRejected");

        if (!File.Exists(filePath))
            return Fail(filePath, methodName, 0, 0, "Fichier introuvable.", "FileNotFound");

        var currentSource = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        if (!currentSource.Contains(originalMethodSource, StringComparison.Ordinal))
            return Fail(filePath, methodName, 0, 0,
                        "Source drift : la méthode a été modifiée depuis la proposition.", "SourceDrift");

        var snapshotId = _rollback.CreateSnapshot(
            $"CodePatcher.{(approvedByHuman ? "Approved" : "Auto")}.{Path.GetFileName(filePath)}.{methodName}",
            new { FilePath = filePath, OriginalSource = currentSource, MethodName = methodName });

        if (string.IsNullOrEmpty(snapshotId))
            return Fail(filePath, methodName, sim.DeltaCC, sim.DeltaLines,
                        "Snapshot Rollback impossible — patch annulé.", "SnapshotFailed");

        ct.ThrowIfCancellationRequested();
        var className = ExtractClassName(filePath);
        var patchedSource = MethodReplacer.ReplaceMethod(
            currentSource, className, methodName, patchedMethodSource);
        if (patchedSource is null)
            return Fail(filePath, methodName, 0, 0,
                "Méthode introuvable dans l'AST — patching impossible.", "AstNotFound");
        await File.WriteAllTextAsync(filePath, patchedSource, ct).ConfigureAwait(false);

        var result = new CodePatchResult(
            Success: true,
            FilePath: filePath,
            MethodName: methodName,
            DeltaCC: sim.DeltaCC,
            DeltaLines: sim.DeltaLines,
            SnapshotId: snapshotId,
            Summary: $"{(approvedByHuman ? "[Approuvé]" : "[Auto]")} Patch appliqué. {sim.Summary} Rollback={snapshotId[..8]}…",
            AppliedAt: DateTimeOffset.UtcNow);

        AddHistory(result);
        LoggingService.LogInfo(
            $"[CodePatcher] ✓ {(approvedByHuman ? "APPROUVÉ" : "Auto")} : {methodName} " +
            $"[ΔCC={sim.DeltaCC:+0;-0}, ΔLignes={sim.DeltaLines:+0;-0}]");

        LoggingService.LogInfo($"[CodePatcherService.ApplyPatchInternalAsync] Exit — result=Success({methodName})");
        return result;
    }

    private void AddPending(CodePatchResult r)
    {
        lock (_histLock)
        {
            _pending.Insert(0, r);
            if (_pending.Count > 50) _pending.RemoveAt(50);
        }
    }

    private void RemovePending(CodePatchResult r)
    {
        lock (_histLock)
            _pending.RemoveAll(p =>
                p.FilePath == r.FilePath &&
                p.MethodName == r.MethodName &&
                p.AppliedAt == r.AppliedAt);
    }

    private void AddHistory(CodePatchResult r)
    {
        lock (_histLock)
        {
            _history.Insert(0, r);
            if (_history.Count > 100) _history.RemoveAt(100);
        }
    }

    private CodePatchResult Fail(
        string filePath, string methodName,
        int deltaCC, int deltaLines,
        string summary, string? error)
    {
        var r = new CodePatchResult(
            Success: false,
            FilePath: filePath,
            MethodName: methodName,
            DeltaCC: deltaCC,
            DeltaLines: deltaLines,
            SnapshotId: string.Empty,
            Summary: summary,
            AppliedAt: DateTimeOffset.UtcNow,
            Error: error);

        AddHistory(r);
        return r;
    }

    private static string ExtractMethodName(string source)
    {
        var idx = source.IndexOf('(');
        if (idx <= 0) return "<unknown>";

        var before = source[..idx].TrimEnd();
        var spaceIdx = before.LastIndexOf(' ');
        return spaceIdx >= 0 ? before[(spaceIdx + 1)..] : before;
    }

    private static string ExtractClassName(string filePath) =>
        Path.GetFileNameWithoutExtension(filePath);
}
