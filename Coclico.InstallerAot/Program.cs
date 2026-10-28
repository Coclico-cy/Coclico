using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Coclico.Installer.Services;

namespace Coclico.Installer;

internal static class Program
{
    public static bool IsUninstallMode { get; private set; }
    public static bool IsSilentMode { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            Environment.CurrentDirectory = Path.GetTempPath();
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }

        IsUninstallMode = args.Any(arg => arg.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) || arg.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
        IsSilentMode = args.Any(arg => arg.Equals("--silent", StringComparison.OrdinalIgnoreCase) || arg.Equals("/silent", StringComparison.OrdinalIgnoreCase));
        NativeLibBundle.ExtractAndLoad();
        AppDomain.CurrentDomain.UnhandledException += (s, e) => LogCrash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (s, e) => LogCrash(e.Exception);
        if (IsSilentMode)
        {
            RunSilent();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static void RunSilent()
    {
        try
        {
            if (IsUninstallMode)
            {
                Task.Run(() => InstallService.FullUninstallAsync(new Progress<InstallProgressReport>())).GetAwaiter().GetResult();
            }
            else
            {
                var config = new Models.InstallConfig();
                Task.Run(() => InstallService.InstallAsync(config, new Progress<InstallProgressReport>())).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LogCrash(ex);
        }
        finally
        {
            Environment.Exit(0);
        }
    }

    internal static void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            string logPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "coclico-setup-crash.txt");
            File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + ex + Environment.NewLine);
        }
        catch (Exception erreurFichier) when (erreurFichier is System.IO.IOException or System.UnauthorizedAccessException) { _ = erreurFichier; }
    }
}
