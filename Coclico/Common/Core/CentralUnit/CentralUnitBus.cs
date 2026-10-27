using System.Collections.Concurrent;
using System.Diagnostics;

namespace Coclico.Services;

public enum CentralRequestPriority
{
    Low,
    Normal,
    Critical
}

public sealed class CentralUnitBus
{
    private const int DefaultMaxCacheEntries = 256;

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, object> _inFlight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Func<object, CancellationToken, Task>> _routes = new(StringComparer.Ordinal);
    private readonly int _maxCacheEntries;
    private long _droppedRequests;
    private long _routedRequests;
    private long _evictedEntries;

    public CentralUnitBus(int maxCacheEntries = DefaultMaxCacheEntries)
    {
        _maxCacheEntries = Math.Max(4, maxCacheEntries);
    }

    public long DroppedRequests => Interlocked.Read(ref _droppedRequests);

    public long RoutedRequests => Interlocked.Read(ref _routedRequests);

    public long EvictedEntries => Interlocked.Read(ref _evictedEntries);

    public async Task<T> QueryAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl, CentralRequestPriority priority = CentralRequestPriority.Normal, CancellationToken cancellationToken = default)
    {
        if (TryGetCached(key, ttl, priority, out T cachedValue))
        {
            return cachedValue;
        }

        Lazy<Task<T>> lazy = new(() => ExecuteFactoryAsync(key, factory, ttl, cancellationToken), LazyThreadSafetyMode.ExecutionAndPublication);
        object winner = _inFlight.GetOrAdd(key, lazy);
        if (!ReferenceEquals(winner, lazy))
        {
            if (winner is Lazy<Task<T>> shared)
            {
                Interlocked.Increment(ref _droppedRequests);
                return await shared.Value.ConfigureAwait(false);
            }

            return await lazy.Value.ConfigureAwait(false);
        }

        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            _ = _inFlight.TryRemove(key, out _);
        }
    }

    public void Invalidate(params string[] keys)
    {
        foreach (string key in keys)
        {
            _ = _cache.TryRemove(key, out _);
        }
    }

    public void RegisterRoute<TCommand>(string route, Func<TCommand, CancellationToken, Task> handler)
    {
        _routes[route] = (command, cancellationToken) => handler((TCommand)command, cancellationToken);
    }

    public Task SendAsync(string route, object command, CancellationToken cancellationToken = default)
    {
        if (!_routes.TryGetValue(route, out Func<object, CancellationToken, Task>? handler))
        {
            throw new KeyNotFoundException($"CentralUnitBus : aucune route enregistrée pour '{route}'.");
        }

        Interlocked.Increment(ref _routedRequests);
        return handler(command, cancellationToken);
    }

    private bool TryGetCached<T>(string key, TimeSpan ttl, CentralRequestPriority priority, out T value)
    {
        value = default!;
        if (!_cache.TryGetValue(key, out CacheEntry? entry) || entry.Value is not T typed)
        {
            return false;
        }

        TimeSpan maxAge = priority switch
        {
            CentralRequestPriority.Low => TimeSpan.FromTicks(ttl.Ticks * 2),
            CentralRequestPriority.Critical => TimeSpan.FromTicks(Math.Max(1, ttl.Ticks / 2)),
            _ => ttl
        };

        if (DateTime.UtcNow - entry.StoredUtc > maxAge)
        {
            return false;
        }

        entry.LastAccessStamp = Stopwatch.GetTimestamp();
        value = typed;
        return true;
    }

    private async Task<T> ExecuteFactoryAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl, CancellationToken cancellationToken)
    {
        T value = await factory(cancellationToken).ConfigureAwait(false);
        _cache[key] = new CacheEntry
        {
            Value = value,
            StoredUtc = DateTime.UtcNow,
            LastAccessStamp = Stopwatch.GetTimestamp()
        };
        EvictIfOverCapacity();
        return value;
    }

    private void EvictIfOverCapacity()
    {
        if (_cache.Count <= _maxCacheEntries)
        {
            return;
        }

        List<KeyValuePair<string, CacheEntry>> ordered = _cache.OrderBy(entry => entry.Value.LastAccessStamp).ToList();
        int excess = _cache.Count - _maxCacheEntries;
        int evicted = ordered
            .Take(excess)
            .Count(entry => _cache.TryRemove(KeyValuePair.Create(entry.Key, entry.Value)));
        _ = Interlocked.Add(ref _evictedEntries, evicted);
    }

    private sealed class CacheEntry
    {
        public object? Value { get; init; }

        public DateTime StoredUtc { get; init; }

        public long LastAccessStamp { get; set; }
    }
}
