namespace Coclico.Modules.Diagnostics.Services.Checks;

using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Rules;
using Coclico.Modules.Diagnostics.Services.Hardware;

public sealed class PowerBatteryCheck : IDiagnosticCheck
{
    private readonly IHardwareTelemetryService _telemetry;

    public PowerBatteryCheck(IHardwareTelemetryService telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        _telemetry = telemetry;
    }

    public string Id => "power.battery";
    public DiagnosticCategory Category => DiagnosticCategory.Power;
    public bool IsQuick => true;
    public bool IsSupported(DiagnosticEnvironment environment) => true;

    public async Task<DiagnosticCheckResult> RunAsync(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        bool batteryPresent = NativeDiag.HasBattery();
        int? charge = null;
        int? runtime = null;
        double? fullLifeRatio = null;
        bool dataAvailable = false;

        if (batteryPresent)
        {
            charge = NativeDiag.GetChargePercent();
            runtime = NativeDiag.GetEstimatedRuntimeMinutes();
            fullLifeRatio = NativeDiag.GetFullLifeRatio();
            dataAvailable = charge is not null;
        }

        BatteryFacts battery = new(
            batteryPresent,
            NativeDiag.IsOnAcPower(),
            charge,
            runtime,
            fullLifeRatio,
            dataAvailable);

        HardwareSensorSnapshot snapshot = await _telemetry.ReadSnapshotAsync(context.CancellationToken).ConfigureAwait(false);
        HardwareSensorValue? thermal = snapshot.Values.FirstOrDefault(v => v.SensorId.StartsWith("thermalzone.", StringComparison.Ordinal));
        ThermalFacts thermalFacts = new(
            thermal is { State: DiagnosticValueState.Measured, Value: not null } ? thermal.Value : null,
            thermal is { State: DiagnosticValueState.Measured, Value: not null });

        List<DiagnosticFinding> findings = [.. DiagnosticRuleEngine.EvaluatePowerAndThermal(battery, thermalFacts)];

        List<DiagnosticEvidence> inventoryEvidence =
        [
            DiagnosticEvidence.Measured("power.batteryPresent", "Diag_Evidence_BatteryPresent", batteryPresent ? "true" : "false", "", "GetSystemPowerStatus"),
            DiagnosticEvidence.Measured("power.onAcPower", "Diag_Evidence_OnAcPower", battery.OnAcPower ? "true" : "false", "", "GetSystemPowerStatus")
        ];

        if (batteryPresent && charge is not null)
        {
            inventoryEvidence.Add(DiagnosticEvidence.Measured("power.chargePercent", "Diag_Evidence_BatteryCharge", charge, "%", "GetSystemPowerStatus"));
        }
        else if (batteryPresent)
        {
            inventoryEvidence.Add(DiagnosticEvidence.Unavailable("power.chargePercent", "Diag_Evidence_BatteryCharge", DiagnosticValueState.NotExposed, "GetSystemPowerStatus"));
        }

        if (!thermalFacts.ThermalZoneAvailable)
        {
            inventoryEvidence.Add(DiagnosticEvidence.Unavailable("thermal.zoneC", "Diag_Evidence_ThermalZone", DiagnosticValueState.NotExposed, "MSAcpi_ThermalZoneTemperature"));
        }

        findings.Add(DiagnosticCheckUtils.InventoryFinding(
            "POWER_INVENTORY_001",
            DiagnosticCategory.Power,
            "Diag_Finding_PowerInventory_Title",
            "Diag_Finding_PowerInventory_Summary",
            inventoryEvidence));

        return DiagnosticCheckUtils.Complete(Id, Category, IsQuick, findings);
    }
}
