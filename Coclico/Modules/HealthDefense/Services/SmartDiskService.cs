using System.Management;

namespace Coclico.Services;

public sealed record SmartDiskStatus(
    string InstanceName,
    bool PredictsFailure,
    uint Reason,
    string Advisory);

public static class SmartDiskService
{
    public static List<SmartDiskStatus> GetDiskStatus()
    {
        List<SmartDiskStatus> results = QueryPhysicalDisks();

        if (results.Count == 0)
        {
            results = QueryFailurePredictStatus();
        }

        LoggingService.LogInfo($"[SmartDiskService] État S.M.A.R.T. récupéré pour {results.Count} disque(s).");
        return results;
    }

    private static List<SmartDiskStatus> QueryPhysicalDisks()
    {
        var results = new List<SmartDiskStatus>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                "SELECT FriendlyName, HealthStatus FROM MSFT_PhysicalDisk");

            foreach (ManagementBaseObject disk in searcher.Get())
            {
                try
                {
                    string instance = disk["FriendlyName"]?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(instance))
                    {
                        instance = "Disque physique";
                    }

                    ushort health = disk["HealthStatus"] is ushort h ? h : ushort.MaxValue;

                    results.Add(new SmartDiskStatus(
                        InstanceName: instance,
                        PredictsFailure: health is 1 or 2,
                        Reason: health,
                        Advisory: health switch
                        {
                            0 => "Aucun signe de panne imminente signalé par Windows pour ce disque.",
                            1 => "Windows signale un avertissement de santé sur ce disque. Surveillez-le et sauvegardez vos données importantes.",
                            2 => "Windows signale ce disque en mauvaise santé. Sauvegardez vos données importantes dès maintenant.",
                            _ => "État de santé inconnu pour ce disque."
                        }));
                }
                finally
                {
                    disk.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or System.IO.IOException or System.UnauthorizedAccessException)
        {
            LoggingService.LogException(ex, "SmartDiskService.QueryPhysicalDisks");
        }

        return results;
    }

    private static List<SmartDiskStatus> QueryFailurePredictStatus()
    {
        var results = new List<SmartDiskStatus>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT InstanceName, PredictFailure, Reason FROM MSStorageDriver_FailurePredictStatus");

            foreach (ManagementBaseObject disk in searcher.Get())
            {
                try
                {
                    string instance = disk["InstanceName"]?.ToString() ?? "Disque inconnu";
                    bool predictsFailure = disk["PredictFailure"] is bool b && b;
                    uint reason = disk["Reason"] is uint r ? r : 0u;

                    results.Add(new SmartDiskStatus(
                        InstanceName: instance,
                        PredictsFailure: predictsFailure,
                        Reason: reason,
                        Advisory: predictsFailure
                            ? "Ce disque signale un risque de panne imminente (S.M.A.R.T.). Sauvegardez vos données importantes dès maintenant."
                            : "Aucun signe de panne imminente signalé par S.M.A.R.T."));
                }
                finally
                {
                    disk.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or System.IO.IOException or System.UnauthorizedAccessException)
        {
            LoggingService.LogException(ex, "SmartDiskService.QueryFailurePredictStatus");
        }

        return results;
    }
}
