using System.Diagnostics;
using System.IO;

namespace Coclico.Services;

public static class BatteryReportService
{
    private static string ReportsDir => Path.Join(SettingsService.SettingsDir, "reports");

    public static async Task<string?> GenerateReportAsync()
    {
        try
        {
            _ = Directory.CreateDirectory(ReportsDir);
            string outputPath = Path.Join(ReportsDir, "battery-report.html");

            try
            {
                File.Delete(outputPath);
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
            {
                LoggingService.LogException(ex, "BatteryReportService.DeleteOldReport");
            }

            IProcessExecutionService? executor = ServiceContainer.GetOptional<IProcessExecutionService>();
            if (executor == null)
            {
                LoggingService.LogInfo("[BatteryReportService] IProcessExecutionService indisponible : conteneur non construit.");
                return null;
            }

            ProcessExecutionResult result = await executor.ExecuteAsync(
                "powercfg.exe",
                ["/batteryreport", "/output", outputPath],
                timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            if (!result.Success || !File.Exists(outputPath))
            {
                LoggingService.LogInfo(
                    $"[BatteryReportService] powercfg a échoué (exit {result.ExitCode}) : " +
                    $"{result.ErrorMessage ?? result.StandardError}");
                return null;
            }

            LoggingService.LogInfo($"[BatteryReportService] Rapport batterie généré : {outputPath}");
            return outputPath;
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "BatteryReportService.GenerateReport");
            return null;
        }
    }

    public static void OpenReport(string reportPath)
    {
        try
        {
            if (!File.Exists(reportPath))
            {
                return;
            }

            _ = Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.IOException or System.InvalidOperationException)
        {
            LoggingService.LogException(ex, "BatteryReportService.OpenReport");
        }
    }
}
