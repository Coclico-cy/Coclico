using System.IO;
using Serilog;

namespace Coclico.Services;

public static class LoggingService
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Coclico", "logs");

    private static readonly object _configLock = new();
    private static bool _serilogConfigured;

    static LoggingService()
    {
        EnsureSerilog();
    }

    public static void EnsureSerilog()
    {
        if (_serilogConfigured)
        {
            return;
        }

        lock (_configLock)
        {
            if (_serilogConfigured)
            {
                return;
            }

            try
            {
                _ = Directory.CreateDirectory(LogDir);
                PurgeLegacyLogs();
            }
            catch { }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    Path.Combine(LogDir, "coclico-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .Enrich.FromLogContext()
                .CreateLogger();

            _serilogConfigured = true;
        }
    }

    public static void LogInfo(string message)
    {
        try { Log.Information("{Message}", message); } catch { }
    }

    public static void LogWarning(string message)
    {
        try { Log.Warning("{Message}", message); } catch { }
    }

    public static void LogError(string message)
    {
        try { Log.Error("{Message}", message); } catch { }
    }

    public static void LogDebug(string message)
    {
        try { Log.Debug("{Message}", message); } catch { }
    }

    public static void LogException(Exception? ex, string? context = null)
    {
        if (ex == null)
        {
            return;
        }

        try
        {
            if (!string.IsNullOrEmpty(context))
            {
                Log.Error(ex, "[{Context}] {Message}", context, ex.Message);
            }
            else
            {
                Log.Error(ex, "{Message}", ex.Message);
            }
        }
        catch { }
    }

    public static Task ShutdownAsync()
    {
        try { Log.CloseAndFlush(); } catch { }
        return Task.CompletedTask;
    }

    private static void PurgeLegacyLogs()
    {
        try
        {
            DateTime cutoff = DateTime.UtcNow.AddDays(-14);
            foreach (string file in Directory.EnumerateFiles(LogDir, "log_*.txt"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch { }
            }
        }
        catch { }
    }
}
