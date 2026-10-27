namespace Coclico.Modules.Diagnostics.Services.Checks;

using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Rules;
using Coclico.Modules.Diagnostics.Services.Hardware;
using Coclico.Services;

public sealed class CpuCheck : IDiagnosticCheck
{
    private const int QuickSamples = 1;
    private const int FullSamples = 3;
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(300);

    private readonly IHardwareTelemetryService _telemetry;

    public CpuCheck(IHardwareTelemetryService telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        _telemetry = telemetry;
    }

    public string Id => "cpu.info";
    public DiagnosticCategory Category => DiagnosticCategory.Cpu;
    public bool IsQuick => true;
    public bool IsSupported(DiagnosticEnvironment environment) => true;

    public async Task<DiagnosticCheckResult> RunAsync(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        int samples = context.Request.Mode == DiagnosticMode.Quick ? QuickSamples : FullSamples;
        string processorName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown";
        double loadMin = double.MaxValue;
        double loadMax = 0;

        for (int i = 0; i < samples; i++)
        {
            if (i > 0)
            {
                await Task.Delay(SampleInterval, context.CancellationToken).ConfigureAwait(false);
            }

            double load = MemoryCleanerService.GetSystemCpuPercent();
            loadMin = Math.Min(loadMin, load);
            loadMax = Math.Max(loadMax, load);
        }

        if (loadMin >= double.MaxValue)
        {
            loadMin = 0;
        }

        (string? topName, double? topPercent) = GetTopCpuProcess();

        double? temperatureC = null;
        bool temperatureAvailable = false;
        HardwareSensorSnapshot snapshot = await _telemetry.ReadSnapshotAsync(context.CancellationToken).ConfigureAwait(false);
        HardwareSensorValue? thermal = snapshot.Values.FirstOrDefault(v => v.SensorId.StartsWith("thermalzone.", StringComparison.Ordinal));
        if (thermal is { State: DiagnosticValueState.Measured, Value: not null })
        {
            temperatureC = thermal.Value;
            temperatureAvailable = true;
        }

        CpuFacts facts = new(
            processorName,
            Environment.ProcessorCount,
            Math.Round(loadMin, 1),
            Math.Round(loadMax, 1),
            temperatureC,
            temperatureAvailable,
            topName,
            topPercent);

        List<DiagnosticFinding> findings = [.. DiagnosticRuleEngine.EvaluateCpu(facts)];
        findings.Add(DiagnosticCheckUtils.InventoryFinding(
            "CPU_INVENTORY_001",
            DiagnosticCategory.Cpu,
            "Diag_Finding_CpuInventory_Title",
            "Diag_Finding_CpuInventory_Summary",
            [
                DiagnosticEvidence.Measured("cpu.name", "Diag_Evidence_CpuName", facts.ProcessorName, "", "Environment"),
                DiagnosticEvidence.Measured("cpu.logicalCores", "Diag_Evidence_CpuCores", facts.LogicalCoreCount, "", "Environment"),
                DiagnosticEvidence.Measured("cpu.loadMax", "Diag_Evidence_CpuLoadMax", facts.LoadPercentMax, "%", "GetSystemTimes", expectedRange: null)
            ]));

        return DiagnosticCheckUtils.Complete(Id, Category, IsQuick, findings);
    }

    private static (string? Name, double? Percent) GetTopCpuProcess()
    {
        try
        {
            using System.Management.ManagementObjectSearcher searcher = new(
                @"root\cimv2",
                "SELECT Name, PercentProcessorTime FROM Win32_PerfFormattedData_PerfProc_Process WHERE Name != '_Total' AND Name != 'Idle' AND PercentProcessorTime > 5");
            string? topName = null;
            double topPercent = 0;

            foreach (System.Management.ManagementBaseObject item in searcher.Get())
            {
                if (item["Name"] is string name
                    && item["PercentProcessorTime"] is string raw
                    && double.TryParse(raw, out double percent)
                    && percent > topPercent)
                {
                    topPercent = percent;
                    topName = name;
                }
            }

            return topName is null ? (null, null) : (topName, Math.Round(topPercent, 1));
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            LoggingService.LogException(ex, "CpuCheck.GetTopCpuProcess");
            return (null, null);
        }
    }
}
