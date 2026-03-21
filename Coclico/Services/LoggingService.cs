using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Coclico.Services
{
    public static class LoggingService
    {
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Coclico", "logs");

        private static string CurrentLogPath =>
            Path.Combine(LogDir, $"log_{DateTime.UtcNow:yyyyMMdd}.txt");

        private static readonly SemaphoreSlim _sem = new(1, 1);

        private static int _rotationDone;

        public static void LogInfo(string message)  => _ = WriteAsync("INFO",  message, null);

        public static void LogError(string message) => _ = WriteAsync("ERROR", message, null);

        public static void LogDebug(string message) => _ = WriteAsync("DEBUG", message, null);

        public static void LogException(Exception ex, string? context = null)
            => _ = WriteAsync("EXC", context ?? ex.Message, ex);

        private static async Task WriteAsync(string level, string message, Exception? ex)
        {
            try
            {
                Directory.CreateDirectory(LogDir);

                if (Interlocked.CompareExchange(ref _rotationDone, 1, 0) == 0)
                {
                    PurgeOldLogs();
                }

                var sb = new StringBuilder();
                sb.Append('[')
                  .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                  .Append($"] [{level}] ")
                  .Append(message);

                if (ex != null)
                {
                    sb.AppendLine();
                    sb.Append(ex.ToString());
                }
                sb.AppendLine();

                await _sem.WaitAsync().ConfigureAwait(false);
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                    using var fs = new FileStream(
                        CurrentLogPath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite,
                        4096,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);

                    await fs.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    await fs.FlushAsync().ConfigureAwait(false);
                }
                finally
                {
                    _sem.Release();
                }
            }
            catch
            {
            }
        }

        private static void PurgeOldLogs()
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-7);
                foreach (var file in Directory.GetFiles(LogDir, "log_*.txt"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoff)
                            File.Delete(file);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
