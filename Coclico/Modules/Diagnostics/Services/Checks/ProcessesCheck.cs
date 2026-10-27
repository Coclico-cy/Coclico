namespace Coclico.Modules.Diagnostics.Services.Checks;

using System.Diagnostics;
using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Rules;
using Coclico.Services;
using Microsoft.Win32;

public sealed class ProcessesCheck : IDiagnosticCheck
{
    private const int MaxStartupEntriesReported = 30;

    public string Id => "processes.startup";
    public DiagnosticCategory Category => DiagnosticCategory.Processes;
    public bool IsQuick => true;
    public bool IsSupported(DiagnosticEnvironment environment) => true;

    public Task<DiagnosticCheckResult> RunAsync(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();

        int processCount = 0;
        string? topCpuName = null;
        double? topCpuPercent = null;
        string? topRamName = null;
        double? topRamPercent = null;

        try
        {
            Process[] processes = Process.GetProcesses();
            processCount = processes.Length;

            long totalRam = MemoryCleanerService.GetRamInfo().TotalPhysBytes;
            long topWorkingSet = 0;
            foreach (Process process in processes)
            {
                using (process)
                {
                    try
                    {
                        if (process.WorkingSet64 > topWorkingSet)
                        {
                            topWorkingSet = process.WorkingSet64;
                            topRamName = process.ProcessName;
                        }
                    }
                    catch (Exception ex) when (ex is not System.OutOfMemoryException) { _ = ex; }
                }
            }

            if (topRamName is not null && totalRam > 0)
            {
                topRamPercent = Math.Round(topWorkingSet * 100.0 / totalRam, 1);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.InvalidOperationException)
        {
            LoggingService.LogException(ex, $"{Id}.Processes");
        }

        (topCpuName, topCpuPercent) = GetTopCpuProcess();

        int startupEntries = CountStartupEntries();

        ProcessFacts facts = new(
            processCount,
            topCpuName,
            topCpuPercent,
            topRamName,
            topRamPercent,
            StartupHealthReadable: startupEntries >= 0,
            StartupHealthy: true,
            StartupHealthMessage: null);

        List<DiagnosticFinding> findings = [.. DiagnosticRuleEngine.EvaluateProcesses(facts)];
        findings.Add(DiagnosticCheckUtils.InventoryFinding(
            "PROCESSES_INVENTORY_001",
            DiagnosticCategory.Processes,
            "Diag_Finding_ProcessesInventory_Title",
            "Diag_Finding_ProcessesInventory_Summary",
            [
                DiagnosticEvidence.Measured("processes.count", "Diag_Evidence_ProcessCount", processCount, "", "Process"),
                DiagnosticEvidence.Measured("processes.topRam", "Diag_Evidence_TopProcess", topRamName ?? "-", "bytes", "Process", topRamName),
                DiagnosticEvidence.Measured("processes.startupEntries", "Diag_Evidence_StartupEntries", Math.Max(0, startupEntries), "", "Registry:RunKey")
            ]));

        return Task.FromResult(DiagnosticCheckUtils.Complete(Id, Category, IsQuick, findings));
    }

    private static int CountStartupEntries()
    {
        int count = 0;
        bool anyRead = false;

        foreach ((RegistryKey root, string subKey) in new[]
        {
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run"),
            (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run"),
            (Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run")
        })
        {
            try
            {
                using RegistryKey? key = root.OpenSubKey(subKey, writable: false);
                if (key is null)
                {
                    continue;
                }

                anyRead = true;
                count += Math.Min(key.ValueCount, MaxStartupEntriesReported);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.IO.IOException) { _ = ex; }
        }

        return anyRead ? count : -1;
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
            LoggingService.LogException(ex, "ProcessesCheck.GetTopCpuProcess");
            return (null, null);
        }
    }
}
