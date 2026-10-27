namespace Coclico.Modules.Diagnostics.Services.Checks;

using Coclico.Modules.Diagnostics.Models;

internal static class DiagnosticCheckUtils
{
    public static DiagnosticCheckResult Complete(
        string checkId,
        DiagnosticCategory category,
        bool isQuick,
        IReadOnlyList<DiagnosticFinding> findings)
        => DiagnosticCheckResult.Finished(
            checkId,
            category,
            isQuick,
            findings.Count > 0 ? DiagnosticCheckStatus.Findings : DiagnosticCheckStatus.Passed,
            findings,
            TimeSpan.Zero);

    public static DiagnosticFinding InventoryFinding(
        string id,
        DiagnosticCategory category,
        string titleKey,
        string summaryKey,
        IReadOnlyList<DiagnosticEvidence> evidence,
        string? entity = null)
        => new()
        {
            Id = id,
            Entity = entity,
            Category = category,
            Severity = DiagnosticSeverity.Info,
            Confidence = DiagnosticConfidence.High,
            TitleKey = titleKey,
            SummaryKey = summaryKey,
            ObservedAtUtc = DateTime.UtcNow,
            Evidence = evidence,
            IsActionable = false
        };
}
