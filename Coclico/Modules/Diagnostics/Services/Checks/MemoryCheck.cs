namespace Coclico.Modules.Diagnostics.Services.Checks;

using System.Diagnostics;
using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Rules;
using Coclico.Services;

public sealed class MemoryCheck : IDiagnosticCheck
{
    public string Id => "memory.pressure";
    public DiagnosticCategory Category => DiagnosticCategory.Memory;
    public bool IsQuick => true;
    public bool IsSupported(DiagnosticEnvironment environment) => true;

    public Task<DiagnosticCheckResult> RunAsync(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();

        MemoryCleanerService.RamInfo ram = MemoryCleanerService.GetRamInfo();
        long commitTotal = Math.Max(0, ram.TotalPageBytes - ram.AvailPageBytes);

        (string? topName, long? topWorkingSet) = GetTopMemoryProcess(context.CancellationToken);

        MemoryFacts facts = new(
            ram.TotalPhysBytes,
            ram.AvailPhysBytes,
            commitTotal,
            ram.TotalPageBytes,
            GetProcessCount(),
            topName,
            topWorkingSet);

        List<DiagnosticFinding> findings = [.. DiagnosticRuleEngine.EvaluateMemory(facts)];
        findings.Add(DiagnosticCheckUtils.InventoryFinding(
            "MEMORY_INVENTORY_001",
            DiagnosticCategory.Memory,
            "Diag_Finding_MemoryInventory_Title",
            "Diag_Finding_MemoryInventory_Summary",
            [
                DiagnosticEvidence.Measured("memory.totalPhysical", "Diag_Evidence_TotalRam", ram.TotalPhysBytes, "bytes", "GlobalMemoryStatusEx"),
                DiagnosticEvidence.Measured("memory.availablePhysical", "Diag_Evidence_AvailableRam", ram.AvailPhysBytes, "bytes", "GlobalMemoryStatusEx"),
                DiagnosticEvidence.Measured("memory.usedPercent", "Diag_Evidence_RamUsedPercent", Math.Round(ram.PhysUsedPercent, 1), "%", "GlobalMemoryStatusEx"),
                DiagnosticEvidence.Measured("memory.processCount", "Diag_Evidence_ProcessCount", facts.ProcessCount, "", "Process")
            ]));

        return Task.FromResult(DiagnosticCheckUtils.Complete(Id, Category, IsQuick, findings));
    }

    private static int GetProcessCount()
    {
        try
        {
            return Process.GetProcesses().Length;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.InvalidOperationException)
        {
            return 0;
        }
    }

    private static (string? Name, long? WorkingSet) GetTopMemoryProcess(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            Process? top = Process.GetProcesses()
                .OrderByDescending(p => SafeWorkingSet(p))
                .FirstOrDefault();

            return top is null ? (null, null) : (top.ProcessName, SafeWorkingSet(top));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.InvalidOperationException or System.OperationCanceledException)
        {
            LoggingService.LogException(ex, "MemoryCheck.GetTopMemoryProcess");
            return (null, null);
        }
    }

    private static long SafeWorkingSet(Process process)
    {
        try
        {
            return process.WorkingSet64;
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            return 0;
        }
    }
}
