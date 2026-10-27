namespace Coclico.Modules.Diagnostics.Services.Checks;

using System.IO;
using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Rules;

public sealed class StorageCheck : IDiagnosticCheck
{
    private const string StorageScope = @"root\Microsoft\Windows\Storage";
    private const string PhysicalDiskQuery = "SELECT DeviceId, Model, MediaType, HealthStatus FROM MSFT_PhysicalDisk";
    private const string ReliabilityQuery = "SELECT DeviceId, Temperature, Wear, ReadErrorsUncorrected, WriteErrorsUncorrected, PowerOnHours FROM MSFT_StorageReliabilityCounter";

    public string Id => "storage.space";
    public DiagnosticCategory Category => DiagnosticCategory.Storage;
    public bool IsQuick => true;
    public bool IsSupported(DiagnosticEnvironment environment) => true;

    public Task<DiagnosticCheckResult> RunAsync(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();

        List<VolumeFacts> volumes = [];
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
            {
                continue;
            }

            volumes.Add(new VolumeFacts(
                drive.Name,
                drive.TotalSize,
                drive.AvailableFreeSpace,
                drive.DriveFormat,
                "Fixed"));
        }

        List<DiskReliabilityFacts> disks = CollectDisks(context.Request.Mode == DiagnosticMode.Quick);

        StorageFacts facts = new(volumes, disks, disks.Count > 0);

        List<DiagnosticFinding> findings = [.. DiagnosticRuleEngine.EvaluateStorage(facts)];

        List<DiagnosticEvidence> inventoryEvidence =
        [
            DiagnosticEvidence.Measured("storage.volumeCount", "Diag_Evidence_VolumeCount", volumes.Count, "", "DriveInfo")
        ];
        foreach (VolumeFacts volume in volumes)
        {
            inventoryEvidence.Add(DiagnosticEvidence.Measured(
                "storage.volume.freePercent", "Diag_Evidence_FreePercent",
                Math.Round(volume.TotalBytes > 0 ? volume.FreeBytes * 100.0 / volume.TotalBytes : 0, 1),
                "%", "DriveInfo", volume.VolumeName));
        }

        if (!facts.DisksEnumerated)
        {
            inventoryEvidence.Add(DiagnosticEvidence.Unavailable(
                "storage.reliabilityCounters", "Diag_Evidence_DiskHealth",
                DiagnosticValueState.NotExposed, "MSFT_StorageReliabilityCounter"));
        }

        findings.Add(DiagnosticCheckUtils.InventoryFinding(
            "STORAGE_INVENTORY_001",
            DiagnosticCategory.Storage,
            "Diag_Finding_StorageInventory_Title",
            "Diag_Finding_StorageInventory_Summary",
            inventoryEvidence));

        return Task.FromResult(DiagnosticCheckUtils.Complete(Id, Category, IsQuick, findings));
    }

    private static List<DiskReliabilityFacts> CollectDisks(bool quick)
    {
        List<DiskReliabilityFacts> disks = [];
        if (quick)
        {
            return disks;
        }

        if (!WmiHelper.TryQuery(StorageScope, PhysicalDiskQuery, out IReadOnlyList<IDictionary<string, object?>> physicalDisks))
        {
            return disks;
        }

        Dictionary<string, IDictionary<string, object?>> reliability = [];
        if (WmiHelper.TryQuery(StorageScope, ReliabilityQuery, out IReadOnlyList<IDictionary<string, object?>> counters))
        {
            foreach (IDictionary<string, object?> counter in counters)
            {
                string? deviceId = WmiHelper.GetString(counter, "DeviceId");
                if (deviceId is not null)
                {
                    reliability[deviceId] = counter;
                }
            }
        }

        foreach (IDictionary<string, object?> disk in physicalDisks)
        {
            string model = WmiHelper.GetString(disk, "Model") ?? "Unknown";
            string? deviceId = WmiHelper.GetString(disk, "DeviceId");
            string? health = WmiHelper.GetString(disk, "HealthStatus");

            long? temperature = null, wear = null, readErrors = null, writeErrors = null, powerOnHours = null;
            if (deviceId is not null && reliability.TryGetValue(deviceId, out IDictionary<string, object?>? counter))
            {
                temperature = WmiHelper.GetInt64(counter, "Temperature");
                wear = WmiHelper.GetInt64(counter, "Wear");
                readErrors = WmiHelper.GetInt64(counter, "ReadErrorsUncorrected");
                writeErrors = WmiHelper.GetInt64(counter, "WriteErrorsUncorrected");
                powerOnHours = WmiHelper.GetInt64(counter, "PowerOnHours");
            }

            disks.Add(new DiskReliabilityFacts(
                model,
                temperature,
                wear,
                readErrors,
                writeErrors,
                powerOnHours,
                health));
        }

        return disks;
    }
}
