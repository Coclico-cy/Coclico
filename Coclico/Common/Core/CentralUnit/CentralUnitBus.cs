using System.Collections.Concurrent;

namespace Coclico.Services;

public enum CentralRequestPriority
{
    Low,
    Normal,
    Critical
}

public sealed class CentralUnitBus
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, object> _inFlight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Func<object, CancellationToken, Task>> _routes = new(StringComparer.Ordinal);
    private long _droppedRequests;
    private long _routedRequests;

    public long DroppedRequests => Interlocked.Read(ref _droppedRequests);

    public long RoutedRequests => Interlocked.Read(ref _routedRequests);

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
        value = typed;
        return true;
    }

    private async Task<T> ExecuteFactoryAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl, CancellationToken cancellationToken)
    {
        T value = await factory(cancellationToken).ConfigureAwait(false);
        _cache[key] = new CacheEntry { Value = value, StoredUtc = DateTime.UtcNow };
        return value;
    }

    private sealed class CacheEntry
    {
        public object? Value { get; init; }

        public DateTime StoredUtc { get; init; }
    }
}
