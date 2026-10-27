using System.Collections.Concurrent;

namespace Coclico.Services;

public static class CentralVariables
{
    public const string ActivityLevel = "Central.Variables.ActivityLevel";
    public const string CpuPercent = "Central.Variables.CpuPercent";
    public const string RamInfo = "Central.Variables.RamInfo";
    public const string GpuInfo = "Central.Variables.GpuInfo";
    public const string NetworkStats = "Central.Variables.NetworkStats";
    public const string TopProcesses = "Central.Variables.TopProcesses";
    public const string DiskUsage = "Central.Variables.DiskUsage";
}

public sealed class CentralUnitVariables
{
    private readonly ConcurrentDictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<Action<string, object?>>> _handlers = new(StringComparer.Ordinal);
    private readonly object _handlerLock = new();
    private long _publishedCount;

    public long PublishedCount => Interlocked.Read(ref _publishedCount);

    public event Action<string, object?>? ValueChanged;

    public void Publish<T>(string key, T value)
    {
        object? boxed = value;
        if (_values.TryGetValue(key, out object? existing) && Equals(existing, boxed))
        {
            return;
        }

        _values[key] = boxed;
        Interlocked.Increment(ref _publishedCount);
        foreach (Action<string, object?> handler in GetHandlers(key))
        {
            try { handler(key, boxed); }
            catch { }
        }

        ValueChanged?.Invoke(key, boxed);
    }

    public T Get<T>(string key)
    {
        if (_values.TryGetValue(key, out object? value) && value is T typed)
        {
            return typed;
        }

        throw new KeyNotFoundException($"CentralUnitVariables : aucune valeur publiée pour '{key}'.");
    }

    public bool TryGet<T>(string key, out T value)
    {
        if (_values.TryGetValue(key, out object? stored) && stored is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    public Action Subscribe(string key, Action<string, object?> handler)
    {
        lock (_handlerLock)
        {
            if (!_handlers.TryGetValue(key, out List<Action<string, object?>>? list) || list is null)
            {
                list = new List<Action<string, object?>>();
                _handlers[key] = list;
            }

            list.Add(handler);
        }

        if (_values.TryGetValue(key, out object? current))
        {
            try { handler(key, current); }
            catch { }
        }

        return () =>
        {
            lock (_handlerLock)
            {
                if (_handlers.TryGetValue(key, out List<Action<string, object?>>? list) && list is not null)
                {
                    _ = list.Remove(handler);
                }
            }
        };
    }

    private List<Action<string, object?>> GetHandlers(string key)
    {
        lock (_handlerLock)
        {
            if (_handlers.TryGetValue(key, out List<Action<string, object?>>? list) && list is not null)
            {
                return new List<Action<string, object?>>(list);
            }

            return new List<Action<string, object?>>();
        }
    }
}
