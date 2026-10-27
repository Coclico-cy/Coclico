namespace Coclico.Modules.Diagnostics.Services;

using System.Text.RegularExpressions;
using Coclico.Modules.Diagnostics.Models;

public sealed partial class DiagnosticPrivacySanitizer
{
    [GeneratedRegex(@"(?i)[A-Z]:\\Users\\[^\\]+\\?")]
    private static partial Regex UserProfilePathRegex();

    [GeneratedRegex(@"(?i)\b(serial(?:\s*number)?\s*[:=]\s*)[A-Z0-9\-]{4,}\b")]
    private static partial Regex SerialLabelRegex();

    public const string UserPlaceholder = "C:\\Users\\***\\";
    public const string MaskedValue = "***";

    public string SanitizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string result = value;
        result = UserProfilePathRegex().Replace(result, UserPlaceholder);
        result = SerialLabelRegex().Replace(result, static m => m.Groups[1].Value + MaskedValue);

        try
        {
            string machineName = Environment.MachineName;
            if (!string.IsNullOrEmpty(machineName))
            {
                result = result.Replace(machineName, MaskedValue, StringComparison.OrdinalIgnoreCase);
            }

            string userName = Environment.UserName;
            if (!string.IsNullOrEmpty(userName) && userName.Length > 2)
            {
                result = result.Replace(userName, MaskedValue, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { _ = ex; }

        return result;
    }

    public IReadOnlyList<DiagnosticFinding> SanitizeFindings(IEnumerable<DiagnosticFinding> findings)
    {
        List<DiagnosticFinding> sanitized = [];
        foreach (DiagnosticFinding finding in findings)
        {
            List<DiagnosticEvidence> evidence = [.. finding.Evidence.Select(SanitizeEvidence)];
            sanitized.Add(finding with
            {
                Entity = finding.Entity is null ? null : SanitizeText(finding.Entity),
                Evidence = evidence
            });
        }

        return sanitized;
    }

    private DiagnosticEvidence SanitizeEvidence(DiagnosticEvidence evidence)
    {
        bool valueIsSensitive = IsSensitiveMetric(evidence.MetricId);
        string? value = valueIsSensitive && evidence.Value is not null
            ? MaskedValue
            : evidence.Value is null
                ? null
                : SanitizeText(evidence.Value);

        return evidence with
        {
            Value = value,
            Entity = evidence.Entity is null ? null : SanitizeText(evidence.Entity),
            RawDetails = evidence.RawDetails is null ? null : SanitizeText(evidence.RawDetails)
        };
    }

    private static bool IsSensitiveMetric(string metricId)
        => metricId.Contains("mac", StringComparison.OrdinalIgnoreCase)
        || metricId.Contains("serial", StringComparison.OrdinalIgnoreCase);
}
