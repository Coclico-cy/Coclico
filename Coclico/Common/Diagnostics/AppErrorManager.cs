using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace Coclico.Services;

public sealed class AppErrorManager : IAppErrorManager, IDisposable
{
    private const int MaxFailuresBeforeDegraded = 3;
    private const int MaxFailuresBeforeFailed = 10;

    private static readonly (int Min, int Max, AppBlock Block, string Prefix)[] CodeRanges =
    [
        (10000, 11000, AppBlock.AI, "E-AI"),
        (11000, 20000, AppBlock.AIRag, "E-RAG"),
        (20000, 30000, AppBlock.Cleaning, "E-CLN"),
        (30000, 40000, AppBlock.HealthDefense, "E-HLT"),
        (40000, 50000, AppBlock.RamCleaner, "E-RAM"),
        (50000, 60000, AppBlock.Apps, "E-APP"),
        (60000, 70000, AppBlock.Installer, "E-INS"),
        (70000, 80000, AppBlock.Security, "E-SEC"),
        (80000, 90000, AppBlock.Power, "E-PWR"),
        (90000, 100000, AppBlock.Settings, "E-CFG"),
        (100000, 110000, AppBlock.UI, "E-UI"),
        (110000, 120000, AppBlock.System, "E-SYS"),
        (120000, 130000, AppBlock.Network, "E-NET"),
        (130000, 140000, AppBlock.Updates, "E-UPD"),
        (140000, 150000, AppBlock.DiskAnalyzer, "E-DSK"),
        (150000, 160000, AppBlock.Dashboard, "E-DSB")
    ];

    private readonly ConcurrentDictionary<AppBlock, BlockState> _states = new();
    private readonly string _errorLogDir;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public event EventHandler<AppBlockError>? BlockError;
    public event EventHandler<string>? BlockRecovered;
    public event EventHandler<AppBlockError>? FatalError;

    public AppErrorManager()
    {
        _errorLogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Coclico", "errors");
        _ = Directory.CreateDirectory(_errorLogDir);
    }

    public void Report(AppErrorCode code, Exception? ex = null, string? detail = null)
    {
        _ = Task.Run(() => ReportAsync(code, ex, detail));
    }

    public async Task ReportAsync(AppErrorCode code, Exception? ex = null, string? detail = null)
    {
        (AppBlock block, string prefix) = Resolve(code);
        BlockState state = _states.GetOrAdd(block, _ => new BlockState());

        int failures = Interlocked.Increment(ref state.FailureCount);
        string message = detail ?? ex?.Message ?? code.ToString();

        var error = new AppBlockError(
            Block: block,
            Code: code,
            Message: message,
            Exception: ex,
            Timestamp: DateTime.UtcNow,
            FailureCount: failures
        );

        if (failures >= MaxFailuresBeforeFailed)
        {
            state.Status = BlockStatus.Failed;
        }
        else if (failures >= MaxFailuresBeforeDegraded)
        {
            state.Status = BlockStatus.Degraded;
        }

        await WriteErrorLogAsync(error, state.Status);

        if (state.Status == BlockStatus.Failed)
        {
            RaiseEvent(FatalError, this, error, nameof(FatalError));
        }
        else
        {
            RaiseEvent(BlockError, this, error, nameof(BlockError));
        }

        LoggingService.LogError($"[{prefix}] [{code}] {message}");
        if (ex != null)
        {
            LoggingService.LogException(ex, $"{block}.{code}");
        }
    }

    public bool IsBlockDegraded(AppBlock block)
    {
        return _states.TryGetValue(block, out BlockState? s) && s.Status != BlockStatus.Healthy;
    }

    public void MarkRecovered(AppBlock block)
    {
        if (_states.TryGetValue(block, out BlockState? state))
        {
            state.Status = BlockStatus.Healthy;
            _ = Interlocked.Exchange(ref state.FailureCount, 0);
            RaiseEvent(BlockRecovered, this, block.ToString(), nameof(BlockRecovered));
        }
    }

    public IReadOnlyDictionary<AppBlock, BlockStatus> GetBlockStatuses()
    {
        var result = new Dictionary<AppBlock, BlockStatus>();
        foreach (KeyValuePair<AppBlock, BlockState> kvp in _states)
        {
            result[kvp.Key] = kvp.Value.Status;
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writeLock.Dispose();
    }

    private async Task WriteErrorLogAsync(AppBlockError error, BlockStatus status)
    {
        try
        {
            await _writeLock.WaitAsync();
            string logFile = Path.Combine(_errorLogDir, $"errors-{DateTime.UtcNow:yyyy-MM-dd}.ndjson");
            string entry = JsonSerializer.Serialize(new
            {
                timestamp = error.Timestamp,
                block = error.Block.ToString(),
                code = (int)error.Code,
                codeName = error.Code.ToString(),
                prefix = Resolve(error.Code).Prefix,
                message = error.Message,
                exception = error.Exception?.ToString(),
                failureCount = error.FailureCount,
                status = status.ToString()
            });
            await File.AppendAllTextAsync(logFile, entry + Environment.NewLine);
        }
        catch (Exception exSwallow)
        {
            LoggingService.LogException(exSwallow, "AppErrorManager.WriteErrorLogAsync");
        }
        finally
        {
            _ = _writeLock.Release();
        }
    }

    private static void RaiseEvent<TArgs>(EventHandler<TArgs>? handler, object sender, TArgs args, string eventName)
    {
        if (handler == null)
        {
            return;
        }

        foreach (Delegate d in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<TArgs>)d).Invoke(sender, args);
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, $"AppErrorManager.{eventName} handler");
            }
        }
    }

    private static (AppBlock Block, string Prefix) Resolve(AppErrorCode code)
    {
        int value = (int)code;
        foreach ((int min, int max, AppBlock block, string prefix) in CodeRanges)
        {
            if (value >= min && value < max)
            {
                return (block, prefix);
            }
        }

        return (AppBlock.System, "E-SYS");
    }

    private sealed class BlockState
    {
        public volatile BlockStatus Status = BlockStatus.Healthy;
        public int FailureCount;
    }
}