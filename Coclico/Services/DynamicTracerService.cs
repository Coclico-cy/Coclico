#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Services.Algorithms;

namespace Coclico.Services;

public sealed class DynamicTracerService : IDynamicTracer, IDisposable
{
    private const int BufferCapacity = 2000;
    private const int FlushIntervalMs = 30_000;
    private const int MaxFilesRetained = 14;

    private readonly ConcurrentQueue<MetricSnapshot> _buffer = new();
    private int _bufferCount;

    private readonly ConcurrentDictionary<string, TDigest> _tDigests = new();

    private readonly string _telemetryDir;
    private readonly Timer _flushTimer;
    private bool _disposed;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public DynamicTracerService()
    {
        _telemetryDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Coclico", "telemetry");
        Directory.CreateDirectory(_telemetryDir);

        _flushTimer = new Timer(_ => FlushToDisk(), null, FlushIntervalMs, FlushIntervalMs);

        LoggingService.LogInfo($"[DynamicTracer] Initialisé — buffer {BufferCapacity}, flush {FlushIntervalMs / 1000}s → {_telemetryDir}");
    }

    public IDisposable BeginOperation(
        string operationName,
        string? category = null,
        IReadOnlyDictionary<string, object>? tags = null)
        => new OperationSpan(this, operationName, category, tags);

    public void Record(
        string operationName,
        TimeSpan elapsed,
        string? category = null,
        IReadOnlyDictionary<string, object>? tags = null)
    {
        var snapshot = new MetricSnapshot(operationName, category, DateTimeOffset.UtcNow, elapsed, tags);
        Enqueue(snapshot);

        _tDigests
            .GetOrAdd(operationName, _ => new TDigest(compression: 100))
            .Add(elapsed.TotalMilliseconds);
    }

    public IReadOnlyList<MetricSnapshot> GetRecentMetrics(int maxCount = 100)
    {
        var all = _buffer.ToArray();
        return all.Length <= maxCount
            ? all
            : all[(all.Length - maxCount)..];
    }

    public IReadOnlyList<MetricSnapshot> GetRecentMetrics(string operationName, int maxCount = 50)
    {
        return _buffer
            .Where(m => m.OperationName == operationName)
            .TakeLast(maxCount)
            .ToList();
    }

    public MetricAggregate? GetAggregate(string operationName)
    {
        var samples = _buffer
            .Where(m => m.OperationName == operationName)
            .Select(m => m.Elapsed)
            .ToArray();

        if (samples.Length == 0) return null;

        TimeSpan p95;
        if (_tDigests.TryGetValue(operationName, out var digest) && digest.Count >= 5)
        {
            p95 = TimeSpan.FromMilliseconds(digest.Quantile(0.95));
        }
        else
        {
            var sorted = samples.OrderBy(e => e).ToArray();
            var p95Index = Math.Max(0, (int)Math.Ceiling(sorted.Length * 0.95) - 1);
            p95 = sorted[p95Index];
        }

        long totalTicks = 0;
        TimeSpan min = samples[0], max = samples[0];
        foreach (var s in samples)
        {
            totalTicks += s.Ticks;
            if (s < min) min = s;
            if (s > max) max = s;
        }
        var avg = TimeSpan.FromTicks(totalTicks / samples.Length);

        return new MetricAggregate(
            OperationName: operationName,
            SampleCount: samples.Length,
            Min: min,
            Max: max,
            Average: avg,
            P95: p95,
            LastObserved: _buffer.Where(m => m.OperationName == operationName)
                                  .Select(m => m.Timestamp).LastOrDefault());
    }

    public void FlushToDisk()
    {
        if (_buffer.IsEmpty) return;

        try
        {
            var snapshots = _buffer.ToArray();
            var payload = new TelemetryFile
            {
                FlushTimestamp = DateTimeOffset.UtcNow,
                Metrics = snapshots.Select(s => new MetricEntry(s)).ToArray(),
            };

            var fileName = $"metrics-{DateTimeOffset.UtcNow:yyyy-MM-dd}.json";
            var filePath = Path.Combine(_telemetryDir, fileName);

            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            File.WriteAllText(filePath, json);

            PruneOldFiles();

            LoggingService.LogInfo($"[DynamicTracer] Flush → {fileName} ({snapshots.Length} métriques)");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "DynamicTracerService.FlushToDisk");
        }
    }

    private void Enqueue(MetricSnapshot snapshot)
    {
        _buffer.Enqueue(snapshot);

        if (Interlocked.Increment(ref _bufferCount) > BufferCapacity)
        {
            _buffer.TryDequeue(out _);
            Interlocked.Decrement(ref _bufferCount);
        }
    }

    private void PruneOldFiles()
    {
        try
        {
            var files = Directory.GetFiles(_telemetryDir, "metrics-*.json")
                .OrderByDescending(f => f)
                .Skip(MaxFilesRetained)
                .ToArray();

            foreach (var f in files)
                File.Delete(f);
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _flushTimer.Dispose();
        FlushToDisk();
    }

    private sealed class OperationSpan(
        DynamicTracerService tracer,
        string operationName,
        string? category,
        IReadOnlyDictionary<string, object>? tags) : IDisposable
    {
        private readonly long _startTimestamp = Stopwatch.GetTimestamp();

        public void Dispose()
        {
            var elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
            tracer.Record(operationName, elapsed, category, tags);
        }
    }

    private sealed class TelemetryFile
    {
        [JsonPropertyName("flushTimestamp")]
        public DateTimeOffset FlushTimestamp { get; set; }

        [JsonPropertyName("metrics")]
        public MetricEntry[] Metrics { get; set; } = [];
    }

    private sealed class MetricEntry(MetricSnapshot s)
    {
        [JsonPropertyName("op")]
        public string OperationName { get; } = s.OperationName;

        [JsonPropertyName("cat")]
        public string? Category { get; } = s.Category;

        [JsonPropertyName("ts")]
        public DateTimeOffset Timestamp { get; } = s.Timestamp;

        [JsonPropertyName("elapsedMs")]
        public double ElapsedMs { get; } = s.Elapsed.TotalMilliseconds;

        [JsonPropertyName("tags")]
        public IReadOnlyDictionary<string, object>? Tags { get; } = s.Tags;
    }
}
