namespace Coclico.Modules.Diagnostics.Services.Checks;

using System.Globalization;
using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Rules;

public sealed class GpuCheck : IDiagnosticCheck
{
    public string Id => "gpu.info";
    public DiagnosticCategory Category => DiagnosticCategory.Gpu;
    public bool IsQuick => true;
    public bool IsSupported(DiagnosticEnvironment environment) => true;

    public Task<DiagnosticCheckResult> RunAsync(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<GpuAdapterFacts> adapters = [];
        if (WmiHelper.TryQuery(@"root\cimv2", "SELECT Name, DriverVersion, AdapterRAM, Status FROM Win32_VideoController", out IReadOnlyList<IDictionary<string, object?>> rows))
        {
            foreach (IDictionary<string, object?> row in rows)
            {
                adapters.Add(new GpuAdapterFacts(
                    WmiHelper.GetString(row, "Name") ?? "Unknown",
                    WmiHelper.GetString(row, "DriverVersion"),
                    WmiHelper.GetInt64(row, "AdapterRAM"),
                    WmiHelper.GetString(row, "Status")));
            }
        }

        List<DiagnosticFinding> findings = [];
        foreach (GpuAdapterFacts adapter in adapters)
        {
            findings.Add(DiagnosticCheckUtils.InventoryFinding(
                "GPU_INVENTORY_001",
                DiagnosticCategory.Gpu,
                "Diag_Finding_GpuInventory_Title",
                "Diag_Finding_GpuInventory_Summary",
                [
                    DiagnosticEvidence.Measured("gpu.name", "Diag_Evidence_GpuName", adapter.Name, "", "Win32_VideoController", adapter.Name),
                    DiagnosticEvidence.Measured("gpu.driverVersion", "Diag_Evidence_GpuDriver", adapter.DriverVersion ?? "-", "", "Win32_VideoController", adapter.Name),
                    DiagnosticEvidence.Measured("gpu.status", "Diag_Evidence_GpuStatus", adapter.Status ?? "-", "", "Win32_VideoController", adapter.Name),
                    DiagnosticEvidence.Measured(
                        "gpu.adapterRamGb", "Diag_Evidence_GpuRam",
                        adapter.AdapterRamBytes is > 0 ? Math.Round(adapter.AdapterRamBytes.Value / (1024.0 * 1024 * 1024), 1).ToString(CultureInfo.InvariantCulture) : "-",
                        "GiB", "Win32_VideoController", adapter.Name)
                ],
                adapter.Name));
        }

        if (adapters.Count == 0)
        {
            findings.Add(new DiagnosticFinding
            {
                Id = "GPU_UNAVAILABLE_001",
                Category = DiagnosticCategory.Gpu,
                Severity = DiagnosticSeverity.Info,
                Confidence = DiagnosticConfidence.Low,
                TitleKey = "Diag_Finding_GpuUnavailable_Title",
                SummaryKey = "Diag_Finding_GpuUnavailable_Summary",
                ObservedAtUtc = DateTime.UtcNow,
                Evidence = [DiagnosticEvidence.Unavailable("gpu.adapters", "Diag_Evidence_GpuName", DiagnosticValueState.NotExposed, "Win32_VideoController")],
                IsActionable = false
            });
        }

        return Task.FromResult(DiagnosticCheckUtils.Complete(Id, Category, IsQuick, findings));
    }
}
