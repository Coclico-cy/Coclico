namespace Coclico.Modules.Diagnostics.Services.Hardware;

using System.Management;
using Coclico.Modules.Diagnostics.Models;
using Coclico.Services;

public sealed class WmiHardwareTelemetryService : IHardwareTelemetryService
{
    private const string Subsystem = "WmiHardwareTelemetry";
    private const string ThermalZoneQuery = "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature";

    public Task<HardwareSensorSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        List<HardwareSensorValue> values = [];
        bool anySourceAvailable = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using ManagementObjectSearcher searcher = new(@"root\wmi", ThermalZoneQuery);
            foreach (ManagementBaseObject item in searcher.Get())
            {
                object? raw = item["CurrentTemperature"];
                if (raw is not null && !Convert.IsDBNull(raw))
                {
                    double kelvin = Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
                    double celsius = kelvin / 10.0 - 273.15;
                    if (!double.IsNaN(celsius) && !double.IsInfinity(celsius) && celsius is > -50 and < 150)
                    {
                        values.Add(new HardwareSensorValue(
                            $"thermalzone.{item["InstanceName"]}",
                            "Diag_Evidence_ThermalZone",
                            Math.Round(celsius, 1),
                            "°C",
                            DiagnosticValueState.Measured));
                        anySourceAvailable = true;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            LoggingService.LogException(ex, $"{Subsystem}.ReadThermalZone");
        }

        HardwareSensorSnapshot snapshot = new(
            DateTime.UtcNow,
            values,
            anySourceAvailable);
        return Task.FromResult(snapshot);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
