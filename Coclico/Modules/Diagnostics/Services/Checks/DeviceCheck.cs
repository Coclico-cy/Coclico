namespace Coclico.Modules.Diagnostics.Services.Checks;

using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Rules;

public sealed class DeviceCheck : IDiagnosticCheck
{
    private const int MaxReportedDevices = 20;
    private const string Query = "SELECT Name, Description, PNPClass, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0";

    public string Id => "devices.pnp";
    public DiagnosticCategory Category => DiagnosticCategory.Devices;
    public bool IsQuick => true;
    public bool IsSupported(DiagnosticEnvironment environment) => true;

    public Task<DiagnosticCheckResult> RunAsync(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();

        List<DeviceProblemFacts> problems = [];
        if (WmiHelper.TryQuery(@"root\cimv2", Query, out IReadOnlyList<IDictionary<string, object?>> rows))
        {
            foreach (IDictionary<string, object?> row in rows.Take(MaxReportedDevices))
            {
                problems.Add(new DeviceProblemFacts(
                    WmiHelper.GetString(row, "Name") ?? WmiHelper.GetString(row, "Description") ?? "Unknown",
                    WmiHelper.GetString(row, "PNPClass"),
                    (int)(WmiHelper.GetInt64(row, "ConfigManagerErrorCode") ?? 0)));
            }
        }

        IReadOnlyList<DiagnosticFinding> findings = DiagnosticRuleEngine.EvaluateDevices(problems);

        List<DiagnosticEvidence> inventoryEvidence =
        [
            DiagnosticEvidence.Measured("devices.problemCount", "Diag_Evidence_DeviceProblemCount", problems.Count, "", "Win32_PnPEntity")
        ];

        List<DiagnosticFinding> all = [.. findings, DiagnosticCheckUtils.InventoryFinding(
            "DEVICES_INVENTORY_001",
            DiagnosticCategory.Devices,
            "Diag_Finding_DevicesInventory_Title",
            "Diag_Finding_DevicesInventory_Summary",
            inventoryEvidence)];

        return Task.FromResult(DiagnosticCheckUtils.Complete(Id, Category, IsQuick, all));
    }
}
