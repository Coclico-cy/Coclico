namespace Coclico.Modules.Diagnostics.Services;

using Coclico.Modules.Diagnostics.Models;

public static class DiagnosticSummaryBuilder
{
    public static DiagnosticSummary Build(DiagnosticRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        int critical = 0, error = 0, warning = 0, info = 0, success = 0;
        foreach (DiagnosticFinding finding in run.Findings)
        {
            switch (finding.Severity)
            {
                case DiagnosticSeverity.Critical: critical++; break;
                case DiagnosticSeverity.Error: error++; break;
                case DiagnosticSeverity.Warning: warning++; break;
                case DiagnosticSeverity.Info: info++; break;
                case DiagnosticSeverity.Success: success++; break;
            }
        }

        int completed = 0, skipped = 0, failed = 0;
        foreach (DiagnosticCheckResult check in run.Checks)
        {
            switch (check.Status)
            {
                case DiagnosticCheckStatus.Passed or DiagnosticCheckStatus.Findings:
                    completed++;
                    break;
                case DiagnosticCheckStatus.Skipped or DiagnosticCheckStatus.Unsupported or DiagnosticCheckStatus.Cancelled:
                    skipped++;
                    break;
                case DiagnosticCheckStatus.Failed or DiagnosticCheckStatus.TimedOut:
                    failed++;
                    break;
            }
        }

        return new DiagnosticSummary(
            ChecksTotal: run.Checks.Count,
            ChecksCompleted: completed,
            ChecksSkipped: skipped,
            ChecksFailed: failed,
            FindingsCritical: critical,
            FindingsError: error,
            FindingsWarning: warning,
            FindingsInfo: info,
            FindingsSuccess: success);
    }
}
