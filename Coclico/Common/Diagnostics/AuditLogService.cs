using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Coclico.Services;

public sealed class AuditLogService : IAuditLog, IDisposable
{
    private const int ChannelCapacity = 10000;
    private const int TailReadBytes = 256 * 1024;

    private readonly string _auditDir;
    private readonly Channel<AuditEntry> _channel = Channel.CreateBounded<AuditEntry>(
        new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });
    private readonly Task _consumer;
    private readonly CancellationTokenSource _cts = new();

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AuditLogService()
    {
        _auditDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Coclico", "audit");
        _ = Directory.CreateDirectory(_auditDir);
        _consumer = Task.Run(ConsumeAuditAsync);
        LoggingService.LogInfo($"[AuditLog] Initialisé (Mode Asynchrone Channel) → {_auditDir}");
    }

    public Task LogAsync(AuditEntry entry)
    {
        return _channel.Writer.WriteAsync(entry).AsTask();
    }

    public async Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int maxCount = 100)
    {
        var entries = new List<AuditEntry>(maxCount);

        try
        {
            string[] files = Directory.EnumerateFiles(_auditDir, "audit-*.log")
                .OrderByDescending(f => f)
                .ToArray();

            foreach (string file in files)
            {
                if (entries.Count >= maxCount)
                {
                    break;
                }

                await foreach (string line in ReadLinesFromEndAsync(file).ConfigureAwait(false))
                {
                    if (entries.Count >= maxCount)
                    {
                        break;
                    }

                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                    {
                        continue;
                    }

                    try
                    {
                        AuditEntry? entry = JsonSerializer.Deserialize<AuditEntry>(trimmed, _json);
                        if (entry != null)
                        {
                            entries.Add(entry);
                        }
                    }
                    catch (Exception exSwallow)
                    {
                        LoggingService.LogException(exSwallow, "AuditLogService.DeserializeEntry");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AuditLogService.GetRecentAsync");
        }

        return entries.AsReadOnly();
    }

    public void Prune(TimeSpan olderThan)
    {
        try
        {
            DateTime cutoff = DateTime.UtcNow - olderThan;
            foreach (string file in Directory.EnumerateFiles(_auditDir, "audit-*.log"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name.Length > 6 &&
                    DateTime.TryParseExact(name["audit-".Length..], "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out DateTime fileDate) &&
                    fileDate < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AuditLogService.Prune");
        }
    }

    public void Dispose()
    {
        _ = _channel.Writer.TryComplete();
        _cts.Cancel();
        try
        {
            _ = _consumer.Wait(1000);
        }
        catch (Exception exSwallow)
        {
            LoggingService.LogException(exSwallow, "AuditLogService.Dispose");
        }
        _cts.Dispose();
    }

    private async Task ConsumeAuditAsync()
    {
        string? currentPath = null;
        FileStream? stream = null;
        StreamWriter? writer = null;

        try
        {
            await foreach (AuditEntry entry in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                string expectedPath = CurrentAuditFilePath();
                if (currentPath != expectedPath)
                {
                    writer?.Dispose();
                    stream?.Dispose();
                    currentPath = expectedPath;
                    stream = new FileStream(currentPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, useAsync: true);
                    writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = false };
                }

                if (writer != null)
                {
                    string json = JsonSerializer.Serialize(entry, _json);
                    await writer.WriteLineAsync(json).ConfigureAwait(false);
                    if (_channel.Reader.Count == 0)
                    {
                        await writer.FlushAsync().ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AuditLogService] Consumer error: {ex}");
        }
        finally
        {
            try
            {
                if (writer != null)
                {
                    await writer.FlushAsync().ConfigureAwait(false);
                    writer.Dispose();
                }
                stream?.Dispose();
            }
            catch (Exception exSwallow)
            {
                LoggingService.LogException(exSwallow, "AuditLogService.ConsumerCleanup");
            }
        }
    }

    private static async IAsyncEnumerable<string> ReadLinesFromEndAsync(string filePath)
    {
        string[] allLines = [];
        if (!File.Exists(filePath))
        {
            yield break;
        }

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
            long tailLength = Math.Min(stream.Length, TailReadBytes);
            stream.Seek(-tailLength, SeekOrigin.End);

            using var reader = new StreamReader(stream, Encoding.UTF8);
            string? content = await reader.ReadToEndAsync().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(content))
            {
                allLines = content.Split('\n');
            }
        }
        catch (Exception exSwallow)
        {
            LoggingService.LogException(exSwallow, "AuditLogService.TailRead");
        }

        for (int i = allLines.Length - 1; i >= 0; i--)
        {
            yield return allLines[i];
        }
    }

    private string CurrentAuditFilePath()
    {
        string today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        return Path.Combine(_auditDir, $"audit-{today}.log");
    }
}
