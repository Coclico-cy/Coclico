using System.Diagnostics;
using System.Net;
using System.ServiceProcess;

namespace Coclico.Services;

public sealed class StartupHealthService
{
    private static readonly string[] CriticalServices = { "wuauserv", "bits", "dosvc" };

    private bool _lastWingetAvailable;
    private bool _lastInternetAvailable;

    public class HealthReport
    {
        public bool IsHealthy { get; set; } = true;
        public string Message { get; set; } = string.Empty;
        public bool RequiresAdmin { get; set; }
    }

    public async Task<HealthReport> CheckAndRepairAsync(bool autoRepair = false)
    {
        var report = new HealthReport();

        bool internetOk = await IsConnectedToInternetAsync();
        _lastInternetAvailable = internetOk;
        if (!internetOk)
        {
            report.IsHealthy = false;
            report.Message = "Aucune connexion Internet détectée. Winget ne fonctionnera pas.";
            return report;
        }

        foreach (string svcName in CriticalServices)
        {
            try
            {
                using var sc = new ServiceController(svcName);
                if (sc.Status != ServiceControllerStatus.Running)
                {
                    LoggingService.LogInfo($"[StartupHealthService] Le service '{svcName}' est dans l'état '{sc.Status}'. AutoRepair={autoRepair}");

                    if (autoRepair)
                    {
                        try
                        {
                            if (sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.Paused)
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
                        catch (Exception ex) when (ex is not System.OutOfMemoryException)
                        {
                            LoggingService.LogException(ex, $"StartupHealthService.CheckService({svcName})");
                            report.IsHealthy = false;
                            report.Message = $"Impossible de démarrer le service critique : {svcName}";
                            return report;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not System.OutOfMemoryException)
            {
                LoggingService.LogException(ex, $"StartupHealthService.CheckService({svcName})");
            }
        }

        bool wingetOk = await IsWingetOperational();
        _lastWingetAvailable = wingetOk;
        if (!wingetOk)
        {
            report.IsHealthy = false;
            report.Message = "Winget n'est pas détecté ou ne répond pas.";
            return report;
        }

        report.Message = "Système opérationnel.";
        return report;
    }

    private async Task<bool> IsConnectedToInternetAsync()
    {
        try
        {
            IPHostEntry entry = await Dns.GetHostEntryAsync("www.microsoft.com");
            return entry.AddressList.Length > 0;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException) { return false; }
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
            if (proc == null)
            {
                return false;
            }

            using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await proc.WaitForExitAsync(exitCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(); } catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "StartupHealthService.IsWingetOperational"); }
                return false;
            }

            return proc.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.InvalidOperationException or System.OperationCanceledException) { return false; }
    }

    public void ResetWingetCache()
    {
        _lastWingetAvailable = false;
    }
}
