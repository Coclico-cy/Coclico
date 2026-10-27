using System.IO;
using Serilog;

namespace Coclico.Services;

public static class LoggingService
{
    private static readonly string LogDir = Path.Join(
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
            catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }

            Log.Logger = new LoggerConfiguration()
#if DEBUG
                .MinimumLevel.Debug()
#else
                .MinimumLevel.Information()
#endif
                .WriteTo.File(
                    Path.Join(LogDir, "coclico-.log"),
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
        try { Log.Information("{Message}", message); } catch (Exception ex) when (ex is not System.OutOfMemoryException) { _ = ex; }
    }

    public static void LogWarning(string message)
    {
        try { Log.Warning("{Message}", message); } catch (Exception ex) when (ex is not System.OutOfMemoryException) { _ = ex; }
    }

    public static void LogError(string message)
    {
        try { Log.Error("{Message}", message); } catch (Exception ex) when (ex is not System.OutOfMemoryException) { _ = ex; }
    }

    public static void LogDebug(string message)
    {
        try { Log.Debug("{Message}", message); } catch (Exception ex) when (ex is not System.OutOfMemoryException) { _ = ex; }
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
        catch (Exception erreurLog) when (erreurLog is not System.OutOfMemoryException) { _ = erreurLog; }
    }

    public static Task ShutdownAsync()
    {
        try { Log.CloseAndFlush(); } catch (Exception ex) when (ex is not System.OutOfMemoryException) { _ = ex; }
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
                catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }
    }
}
