#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Coclico.Services;

public sealed class AuditLogService : IAuditLog, IDisposable
{
    private readonly string _auditDir;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AuditLogService()
    {
        _auditDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Coclico", "audit");
        Directory.CreateDirectory(_auditDir);
        LoggingService.LogInfo($"[AuditLog] Initialisé → {_auditDir}");
    }

    public async Task LogAsync(AuditEntry entry)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var filePath = CurrentAuditFilePath();
            var line = JsonSerializer.Serialize(entry, _json) + "\n";
            await File.AppendAllTextAsync(filePath, line).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AuditLogService.LogAsync");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int maxCount = 100)
    {
        var entries = new List<AuditEntry>(maxCount);

        try
        {
            var files = Directory.GetFiles(_auditDir, "audit-*.log")
                .OrderByDescending(f => f)
                .ToArray();

            foreach (var file in files)
            {
                if (entries.Count >= maxCount) break;

                await foreach (var line in ReadLinesFromEndAsync(file).ConfigureAwait(false))
                {
                    if (entries.Count >= maxCount) break;
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    try
                    {
                        var entry = JsonSerializer.Deserialize<AuditEntry>(trimmed, _json);
                        if (entry != null) entries.Add(entry);
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AuditLogService.GetRecentAsync");
        }

        return entries.AsReadOnly();
    }

    private static async IAsyncEnumerable<string> ReadLinesFromEndAsync(string filePath)
    {
        const int bufSize = 4096;

        if (!File.Exists(filePath)) yield break;

        await using var fs = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: bufSize, useAsync: true);

        long remaining = fs.Length;
        if (remaining == 0) yield break;

        string carry = string.Empty;

        var buf = ArrayPool<byte>.Shared.Rent(bufSize);
        try
        {
            while (remaining > 0)
            {
                int toRead = (int)Math.Min(bufSize, remaining);
                remaining -= toRead;
                fs.Seek(remaining, SeekOrigin.Begin);

                int read = 0;
                while (read < toRead)
                {
                    int n = await fs.ReadAsync(buf.AsMemory(read, toRead - read)).ConfigureAwait(false);
                    if (n == 0) break;
                    read += n;
                }

                var blockText = System.Text.Encoding.UTF8.GetString(buf, 0, read) + carry;
                carry = string.Empty;

                int end = blockText.Length;
                for (int i = blockText.Length - 1; i >= 0; i--)
                {
                    if (blockText[i] != '\n') continue;
                    var segment = blockText.Substring(i + 1, end - i - 1).TrimEnd('\r');
                    if (segment.Length > 0) yield return segment;
                    end = i;
                }

                carry = blockText.Substring(0, end);
            }

            if (carry.Length > 0)
            {
                var first = carry.TrimEnd('\r', '\n');
                if (first.Length > 0) yield return first;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    private string CurrentAuditFilePath()
    {
        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        return Path.Combine(_auditDir, $"audit-{today}.log");
    }

    public void Prune(TimeSpan olderThan)
    {
        try
        {
            var cutoff = DateTime.UtcNow - olderThan;
            foreach (var file in Directory.GetFiles(_auditDir, "audit-*.log"))
            {
                if (File.GetCreationTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AuditLogService.Prune");
        }
    }

    public void Dispose()
    {
        _writeLock.Dispose();
    }
}
