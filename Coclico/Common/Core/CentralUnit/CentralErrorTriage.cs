namespace Coclico.Services;

public sealed class CentralErrorEntry
{
    public required string Context { get; init; }

    public required string TypeName { get; init; }

    public required string Message { get; init; }

    public required DateTime FirstSeenUtc { get; init; }

    public required DateTime LastSeenUtc { get; init; }

    public long Count { get; init; }
}

public sealed class CentralErrorTriage
{
    private const int MaxDistinctEntries = 256;

    public static CentralErrorTriage Current { get; } = new();

    private readonly object _stateLock = new();
    private readonly Dictionary<string, ErrorAggregate> _entries = new(StringComparer.Ordinal);
    private long _totalRecords;

    public long TotalRecords => Interlocked.Read(ref _totalRecords);

    public int DistinctCount
    {
        get
        {
            lock (_stateLock)
            {
                return _entries.Count;
            }
        }
    }

    public void Record(Exception? exception, string context)
    {
        if (exception is null)
        {
            return;
        }

        string typeName = exception.GetType().FullName ?? exception.GetType().Name;
        string message = exception.Message;
        string key = $"{context}|{typeName}|{message}";

        lock (_stateLock)
        {
            _totalRecords++;
            DateTime now = DateTime.UtcNow;
            if (_entries.TryGetValue(key, out ErrorAggregate? existing))
            {
                existing.Count++;
                existing.LastSeenUtc = now;
            }
            else
            {
                _entries[key] = new ErrorAggregate(context, typeName, message, now, now);
            }

            if (_entries.Count > MaxDistinctEntries)
            {
                TrimToCapacity();
            }
        }
    }

    public List<CentralErrorEntry> GetTop(int max = 10)
    {
        lock (_stateLock)
        {
            return _entries.Values
                .OrderByDescending(entry => entry.Count)
                .ThenByDescending(entry => entry.LastSeenUtc)
                .Take(Math.Max(1, max))
                .Select(entry => new CentralErrorEntry
                {
                    Context = entry.Context,
                    TypeName = entry.TypeName,
                    Message = entry.Message,
                    FirstSeenUtc = entry.FirstSeenUtc,
                    LastSeenUtc = entry.LastSeenUtc,
                    Count = entry.Count
                })
                .ToList();
        }
    }

    public void Clear()
    {
        lock (_stateLock)
        {
            _entries.Clear();
            _ = Interlocked.Exchange(ref _totalRecords, 0);
        }
    }

    private void TrimToCapacity()
    {
        List<KeyValuePair<string, ErrorAggregate>> ordered = _entries
            .OrderBy(entry => entry.Value.LastSeenUtc)
            .ToList();

        foreach (KeyValuePair<string, ErrorAggregate> entry in ordered.Take(_entries.Count - MaxDistinctEntries))
        {
            _ = _entries.Remove(entry.Key);
        }
    }

    private sealed class ErrorAggregate(string context, string typeName, string message, DateTime firstSeenUtc, DateTime lastSeenUtc)
    {
        public string Context { get; } = context;

        public string TypeName { get; } = typeName;

        public string Message { get; } = message;

        public DateTime FirstSeenUtc { get; } = firstSeenUtc;

        public DateTime LastSeenUtc { get; set; } = lastSeenUtc;

        public long Count { get; set; } = 1;
    }
}
