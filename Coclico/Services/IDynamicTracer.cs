#nullable enable
using System;
using System.Collections.Generic;

namespace Coclico.Services;

public sealed record MetricSnapshot(
    string OperationName,
    string? Category,
    DateTimeOffset Timestamp,
    TimeSpan Elapsed,
    IReadOnlyDictionary<string, object>? Tags = null);

public sealed record MetricAggregate(
    string OperationName,
    int SampleCount,
    TimeSpan Min,
    TimeSpan Max,
    TimeSpan Average,
    TimeSpan P95,
    DateTimeOffset LastObserved);

public interface IDynamicTracer
{
    IDisposable BeginOperation(
        string operationName,
        string? category = null,
        IReadOnlyDictionary<string, object>? tags = null);

    void Record(
        string operationName,
        TimeSpan elapsed,
        string? category = null,
        IReadOnlyDictionary<string, object>? tags = null);

    IReadOnlyList<MetricSnapshot> GetRecentMetrics(int maxCount = 100);

    IReadOnlyList<MetricSnapshot> GetRecentMetrics(string operationName, int maxCount = 50);

    MetricAggregate? GetAggregate(string operationName);

    void FlushToDisk();
}
