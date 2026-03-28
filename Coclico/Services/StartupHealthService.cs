#nullable enable
using System;
using System.Diagnostics;
using System.Net;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace Coclico.Services;

public class StartupHealthService
{
    private static readonly string[] CriticalServices = { "wuauserv", "bits", "dosvc" };

    private bool _lastWingetAvailable;
    private bool _lastInternetAvailable;

    public class HealthReport
    {
        public bool IsHealthy { get; set; } = true;
        public string Message { get; set; } = string.Empty;
        public bool RequiresAdmin { get; set; } = false;
    }

    public async Task<HealthReport> CheckAndRepairAsync()
    {
        var report = new HealthReport();

        var internetOk = await IsConnectedToInternetAsync();
        _lastInternetAvailable = internetOk;
        if (!internetOk)
        {
            report.IsHealthy = false;
            report.Message = "Aucune connexion Internet détectée. Winget ne fonctionnera pas.";
            return report;
        }

        foreach (var svcName in CriticalServices)
        {
            try
            {
                using var sc = new ServiceController(svcName);
                if (sc.Status != ServiceControllerStatus.Running)
                {
                    try
                    {
                        if (sc.Status == ServiceControllerStatus.Stopped || sc.Status == ServiceControllerStatus.Paused)
                        {
                            sc.Start();
                            await Task.Run(() => sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5)));
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        report.IsHealthy = false;
                        report.RequiresAdmin = true;
                        report.Message = $"Le service '{svcName}' est arrêté et nécessite les droits Admin pour démarrer.";
                        return report;
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogException(ex, $"StartupHealthService.CheckService({svcName})");
                        report.IsHealthy = false;
                        report.Message = $"Impossible de démarrer le service critique : {svcName}";
                        return report;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, $"StartupHealthService.CheckService({svcName})");
            }
        }

        var wingetOk = await IsWingetOperational();
        _lastWingetAvailable = wingetOk;
        if (!wingetOk)
        {
            report.IsHealthy = false;
            report.Message = "Winget n'est pas détecté ou ne répond pas.";
            return report;
        }

        _lastWingetAvailable = true;
        report.Message = "Système opérationnel.";
        return report;
    }

    private async Task<bool> IsConnectedToInternetAsync()
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync("www.microsoft.com");
            return entry.AddressList.Length > 0;
        }
        catch { return false; }
    }

    private async Task<bool> IsWingetOperational()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "--version",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    public SystemHealthSnapshot GetCurrentHealth()
    {
        var ramInfo = MemoryCleanerService.GetRamInfo();
        var rollback = ServiceContainer.GetOptional<IRollbackService>();

        return new SystemHealthSnapshot(
            WingetAvailable: _lastWingetAvailable,
            InternetAvailable: _lastInternetAvailable,
            RollbackSnapshotCount: rollback?.GetSnapshotCount() ?? 0,
            CpuPercent: MemoryCleanerService.GetSystemCpuPercent(),
            MemoryUsedMb: ramInfo.UsedPhysBytes / (1024 * 1024),
            MemoryTotalMb: ramInfo.TotalPhysBytes / (1024 * 1024),
            CheckedAt: DateTimeOffset.UtcNow);
    }
}

public sealed record SystemHealthSnapshot(
    bool WingetAvailable,
    bool InternetAvailable,
    int RollbackSnapshotCount,
    double CpuPercent,
    long MemoryUsedMb,
    long MemoryTotalMb,
    DateTimeOffset CheckedAt);
