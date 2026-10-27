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
    private readonly ConcurrentDictionary<string, Action<string, object?>[]> _handlers = new(StringComparer.Ordinal);
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
        if (_handlers.TryGetValue(key, out Action<string, object?>[]? handlers) && handlers is not null && handlers.Length > 0)
        {
            InvokeHandlers(key, boxed, handlers);
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
            _handlers.TryGetValue(key, out Action<string, object?>[]? current);
            Action<string, object?>[] updated = new Action<string, object?>[(current?.Length ?? 0) + 1];
            if (current is not null && current.Length > 0)
            {
                Array.Copy(current, updated, current.Length);
            }

            updated[^1] = handler;
            _handlers[key] = updated;
        }

        if (_values.TryGetValue(key, out object? currentValue))
        {
            try { handler(key, currentValue); }
            catch (Exception ex) when (ex is not System.OutOfMemoryException) { _ = ex; }
        }

        return () =>
        {
            lock (_handlerLock)
            {
                if (_handlers.TryGetValue(key, out Action<string, object?>[]? existing) && existing is not null)
                {
                    int index = Array.IndexOf(existing, handler);
                    if (index >= 0)
                    {
                        Action<string, object?>[] updated = new Action<string, object?>[existing.Length - 1];
                        Array.Copy(existing, 0, updated, 0, index);
                        Array.Copy(existing, index + 1, updated, index, existing.Length - index - 1);
                        _handlers[key] = updated;
                    }
                }
            }
        };
    }

    private static void InvokeHandlers(string key, object? value, Action<string, object?>[] handlers)
    {
        foreach (Action<string, object?> handler in handlers)
        {
            try { handler(key, value); }
            catch (Exception ex) when (ex is not System.OutOfMemoryException) { _ = ex; }
        }
    }
}
