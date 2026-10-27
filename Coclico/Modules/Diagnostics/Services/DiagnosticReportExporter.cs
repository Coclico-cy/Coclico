namespace Coclico.Modules.Diagnostics.Services;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coclico.Modules.Diagnostics.Models;

public static class DiagnosticReportExporter
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ExportJson(DiagnosticRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.SchemaVersion = CurrentSchemaVersion;
        return JsonSerializer.Serialize(run, JsonOptions);
    }

    public static string ExportText(DiagnosticRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        StringBuilder sb = new();
        sb.AppendLine("Coclico Diagnostic - Report");
        sb.AppendLine($"Schema: {run.SchemaVersion} | Rules: {run.RulesVersion} | App: {run.ApplicationVersion}");
        sb.AppendLine($"Run: {run.Id}");
        sb.AppendLine($"Started (UTC): {run.StartedAtUtc:yyyy-MM-dd HH:mm:ss}");
        if (run.CompletedAtUtc is DateTime completed)
        {
            sb.AppendLine($"Completed (UTC): {completed:yyyy-MM-dd HH:mm:ss}");
        }

        sb.AppendLine($"Mode: {run.Mode} | Status: {run.Status}");

        if (run.SystemSnapshot is DiagnosticSystemSnapshot snapshot)
        {
            sb.AppendLine(
                $"OS: {snapshot.OsVersion} ({snapshot.OsArchitecture}) | Cores: {snapshot.ProcessorCount} | RAM: {snapshot.TotalPhysicalMemoryBytes / (1024.0 * 1024 * 1024):F1} GiB | Elevated: {snapshot.IsElevated}");
        }

        sb.AppendLine();
        sb.AppendLine("NOTE: this report is read-only diagnostics. \"Not available\" means the");
        sb.AppendLine("data is not exposed by this hardware/OS, NOT that everything is fine.");
        sb.AppendLine();

        if (run.Summary is DiagnosticSummary summary)
        {
            sb.AppendLine(
                $"Checks: {summary.ChecksCompleted}/{summary.ChecksTotal} completed, {summary.ChecksSkipped} skipped, {summary.ChecksFailed} failed");
            sb.AppendLine(
                $"Findings: {summary.FindingsCritical} critical, {summary.FindingsError} error, {summary.FindingsWarning} warning, {summary.FindingsInfo} info");
            sb.AppendLine();
        }

        sb.AppendLine("=== FINDINGS ===");
        if (run.Findings.Count == 0)
        {
            sb.AppendLine("No problem detected in the executed checks. This is not a guarantee.");
        }

        foreach (DiagnosticFinding finding in run.Findings)
        {
            sb.AppendLine();
            sb.AppendLine($"[{finding.Severity}] ({finding.Confidence} confidence) {finding.Id}"
                + (finding.Entity is null ? "" : $" - {finding.Entity}"));
            sb.AppendLine($"  Observed (UTC): {finding.ObservedAtUtc:yyyy-MM-dd HH:mm:ss}");

            foreach (DiagnosticEvidence evidence in finding.Evidence)
            {
                string value = evidence.State == DiagnosticValueState.Measured
                    ? $"{evidence.Value ?? ""}{(string.IsNullOrEmpty(evidence.Unit) ? "" : " " + evidence.Unit)}"
                    : $"{evidence.State}";
                sb.AppendLine(
                    $"  - {evidence.MetricId}: {value} (source: {evidence.Source})");
            }

            foreach (string cause in finding.PossibleCauseKeys)
            {
                sb.AppendLine($"  Possible cause (hypothesis): {cause}");
            }

            foreach (string step in finding.RecommendedNextStepKeys)
            {
                sb.AppendLine($"  Next step: {step}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== CHECKS ===");
        foreach (DiagnosticCheckResult check in run.Checks)
        {
            sb.AppendLine(
                $"  {check.CheckId} [{check.Category}] => {check.Status} in {check.Duration.TotalMilliseconds:F0} ms"
                + (string.IsNullOrEmpty(check.Reason) ? "" : $" ({check.Reason})"));
        }

        return sb.ToString();
    }
}
