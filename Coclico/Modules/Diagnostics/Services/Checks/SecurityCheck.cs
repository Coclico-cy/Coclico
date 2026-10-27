namespace Coclico.Modules.Diagnostics.Services.Checks;

using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Rules;
using Coclico.Services;

public sealed class SecurityCheck : IDiagnosticCheck
{
    private readonly SystemHealthService _systemHealth;

    public SecurityCheck(SystemHealthService systemHealth)
    {
        ArgumentNullException.ThrowIfNull(systemHealth);
        _systemHealth = systemHealth;
    }

    public string Id => "security.defender";
    public DiagnosticCategory Category => DiagnosticCategory.Security;
    public bool IsQuick => true;
    public bool IsSupported(DiagnosticEnvironment environment) => true;

    public async Task<DiagnosticCheckResult> RunAsync(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        SystemHealthService.DefenderStatusInfo info = await _systemHealth.GetDefenderStatusAsync().ConfigureAwait(false);

        bool statusReadable = info.LastUpdatedTime is not null
            || (info.SignatureVersion is string version && !version.Contains("inconnu", StringComparison.OrdinalIgnoreCase));

        string? signatureAge = null;
        if (info.LastUpdatedTime is DateTime updated)
        {
            signatureAge = Math.Max(0, (int)(DateTime.UtcNow - updated.ToUniversalTime()).TotalDays).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        DefenderFacts facts = new(
            statusReadable,
            info.IsRealTimeProtectionOn,
            info.IsAntivirusActive,
            info.IsAntivirusActive,
            signatureAge);

        List<DiagnosticFinding> findings = [.. DiagnosticRuleEngine.EvaluateSecurity(facts)];

        List<DiagnosticEvidence> inventoryEvidence = statusReadable
            ?
            [
                DiagnosticEvidence.Measured("defender.realTimeProtection", "Diag_Evidence_Rtp", info.IsRealTimeProtectionOn ? "true" : "false", "", "Get-MpComputerStatus"),
                DiagnosticEvidence.Measured("defender.antivirusEnabled", "Diag_Evidence_Av", info.IsAntivirusActive ? "true" : "false", "", "Get-MpComputerStatus"),
                DiagnosticEvidence.Measured("defender.signatureAgeDays", "Diag_Evidence_SignatureAge", signatureAge ?? "-", "days", "Get-MpComputerStatus")
            ]
            : [DiagnosticEvidence.Unavailable("defender.status", "Diag_Evidence_Av", DiagnosticValueState.NotExposed, "Get-MpComputerStatus")];

        findings.Add(DiagnosticCheckUtils.InventoryFinding(
            "SECURITY_INVENTORY_001",
            DiagnosticCategory.Security,
            "Diag_Finding_SecurityInventory_Title",
            "Diag_Finding_SecurityInventory_Summary",
            inventoryEvidence));

        return DiagnosticCheckUtils.Complete(Id, Category, IsQuick, findings);
    }
}
