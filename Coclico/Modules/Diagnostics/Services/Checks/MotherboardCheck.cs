namespace Coclico.Modules.Diagnostics.Services.Checks;

using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Rules;

public sealed class MotherboardCheck : IDiagnosticCheck
{
    public string Id => "motherboard.info";
    public DiagnosticCategory Category => DiagnosticCategory.Motherboard;
    public bool IsQuick => false;
    public bool IsSupported(DiagnosticEnvironment environment) => true;

    public Task<DiagnosticCheckResult> RunAsync(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();

        List<DiagnosticEvidence> evidence = [];

        if (WmiHelper.TryQuery(@"root\cimv2", "SELECT Manufacturer, Product, Version FROM Win32_BaseBoard", out IReadOnlyList<IDictionary<string, object?>> boards))
        {
            foreach (IDictionary<string, object?> board in boards.Take(3))
            {
                string manufacturer = WmiHelper.GetString(board, "Manufacturer") ?? "-";
                string product = WmiHelper.GetString(board, "Product") ?? "-";
                evidence.Add(DiagnosticEvidence.Measured(
                    "motherboard.model", "Diag_Evidence_Motherboard",
                    $"{manufacturer} {product}".Trim(), "", "Win32_BaseBoard"));
            }
        }

        if (WmiHelper.TryQuery(@"root\cimv2", "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS", out IReadOnlyList<IDictionary<string, object?>> biosList))
        {
            foreach (IDictionary<string, object?> bios in biosList.Take(3))
            {
                string vendor = WmiHelper.GetString(bios, "Manufacturer") ?? "-";
                string version = WmiHelper.GetString(bios, "SMBIOSBIOSVersion") ?? "-";
                evidence.Add(DiagnosticEvidence.Measured(
                    "bios.version", "Diag_Evidence_BiosVersion",
                    $"{vendor} {version}".Trim(), "", "Win32_BIOS"));
            }
        }

        if (evidence.Count == 0)
        {
            evidence.Add(DiagnosticEvidence.Unavailable(
                "motherboard.info", "Diag_Evidence_Motherboard",
                DiagnosticValueState.NotExposed, "Win32_BaseBoard/Win32_BIOS"));
        }

        DiagnosticFinding finding = DiagnosticCheckUtils.InventoryFinding(
            "MOTHERBOARD_INVENTORY_001",
            DiagnosticCategory.Motherboard,
            "Diag_Finding_MotherboardInventory_Title",
            "Diag_Finding_MotherboardInventory_Summary",
            evidence);

        return Task.FromResult(DiagnosticCheckUtils.Complete(Id, Category, IsQuick, [finding]));
    }
}
