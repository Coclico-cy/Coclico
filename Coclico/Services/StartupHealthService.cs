using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace Coclico.Services
{
    public class StartupHealthService
    {
        private static readonly string[] CriticalServices = { "wuauserv", "bits", "dosvc" };

        public class HealthReport
        {
            public bool IsHealthy { get; set; } = true;
            public string Message { get; set; } = string.Empty;
            public bool RequiresAdmin { get; set; } = false;
        }

        public async Task<HealthReport> CheckAndRepairAsync()
        {
            var report = new HealthReport();

            if (!await IsConnectedToInternetAsync())
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
                        catch
                        {
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

            if (!await IsWingetOperational())
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
    }
}
