#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Coclico.Services;

public enum FeatureExecutionStatus
{
    Initializing,
    Running,
    Success,
    Warning,
    Error
}

public sealed class FeatureState(string featureName) : INotifyPropertyChanged
{
    private FeatureExecutionStatus _status = FeatureExecutionStatus.Initializing;
    private DateTime _timestampUtc = DateTime.UtcNow;
    private string? _message = "Waiting";
    private DateTime _startTimeUtc = DateTime.UtcNow;
    private DateTime? _endTimeUtc;

    private const int LogCapacity = 500;
    private readonly CircularBuffer<string> _logBuffer = new(LogCapacity);

    public string FeatureName { get; } = featureName ?? throw new ArgumentNullException(nameof(featureName));

    public FeatureExecutionStatus Status
    {
        get => _status;
        internal set => SetField(ref _status, value);
    }

    public DateTime TimestampUtc
    {
        get => _timestampUtc;
        internal set => SetField(ref _timestampUtc, value);
    }

    public string? Message
    {
        get => _message;
        internal set => SetField(ref _message, value);
    }

    public DateTime StartTimeUtc
    {
        get => _startTimeUtc;
        internal set => SetField(ref _startTimeUtc, value);
    }

    public DateTime? EndTimeUtc
    {
        get => _endTimeUtc;
        internal set => SetField(ref _endTimeUtc, value);
    }

    public TimeSpan Duration => EndTimeUtc.HasValue ? EndTimeUtc.Value - StartTimeUtc : TimeSpan.Zero;

    public IReadOnlyList<string> LogHistory => _logBuffer.ToListNewestFirst();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    internal void AddLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        _logBuffer.Add($"[{DateTime.UtcNow:O}] {message}");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LogHistory)));
    }
}

public sealed class FeatureHealthChangedEventArgs(FeatureState state) : EventArgs
{
    public FeatureState State { get; } = state;
}

public sealed class FeatureExecutionContext(FeatureExecutionEngine engine, string featureName)
{
    public void Report(string message)
    {
        engine.UpdateState(featureName, FeatureExecutionStatus.Running, message);
    }

    public void SetStatus(FeatureExecutionStatus status, string? message = null)
    {
        engine.UpdateState(featureName, status, message);
    }
}

public sealed class FeatureExecutionEngine
{
    private readonly ConcurrentDictionary<string, FeatureState> _stateMap =
        new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<FeatureHealthChangedEventArgs>? HealthChanged;

    internal void UpdateState(string featureName, FeatureExecutionStatus status, string? message = null)
    {
        if (string.IsNullOrWhiteSpace(featureName))
            throw new ArgumentException("Feature name cannot be null or whitespace.", nameof(featureName));

        var state = _stateMap.GetOrAdd(featureName, name => new FeatureState(name));
        var isInitialize = status == FeatureExecutionStatus.Initializing;

        if (isInitialize)
        {
            state.StartTimeUtc = DateTime.UtcNow;
            state.EndTimeUtc = null;
        }
        else if (status is FeatureExecutionStatus.Success or FeatureExecutionStatus.Warning or FeatureExecutionStatus.Error)
        {
            if (!state.EndTimeUtc.HasValue)
                state.EndTimeUtc = DateTime.UtcNow;
        }

        state.Status = status;
        state.Message = message;
        state.TimestampUtc = DateTime.UtcNow;
        state.AddLog($"Status={status}; Message={message ?? ""}");
        HealthChanged?.Invoke(this, new FeatureHealthChangedEventArgs(state));
    }

    public FeatureState? GetState(string featureName)
    {
        if (string.IsNullOrWhiteSpace(featureName))
            return null;

        _stateMap.TryGetValue(featureName, out var state);
        return state;
    }

    public IReadOnlyCollection<FeatureState> GetAllStates() => _stateMap.Values.ToList().AsReadOnly();

    public async Task RunFeatureAsync(string featureName, Func<Task> action)
    {
        if (string.IsNullOrWhiteSpace(featureName))
            throw new ArgumentException("Feature name cannot be null or whitespace.", nameof(featureName));
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        await RunFeatureAsync<object>(featureName, async ctx =>
        {
            await action().ConfigureAwait(false);
            return default!;
        }).ConfigureAwait(false);
    }

    public async Task<T> RunFeatureAsync<T>(string featureName, Func<FeatureExecutionContext, Task<T>> action)
    {
        if (string.IsNullOrWhiteSpace(featureName))
            throw new ArgumentException("Feature name cannot be null or whitespace.", nameof(featureName));
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        var context = new FeatureExecutionContext(this, featureName);

        try
        {
            UpdateState(featureName, FeatureExecutionStatus.Initializing, "Initializing");
            LoggingService.LogInfo($"[FeatureExecutionEngine] Starting feature '{featureName}'.");

            UpdateState(featureName, FeatureExecutionStatus.Running, "Running");
            var result = await action(context).ConfigureAwait(false);

            UpdateState(featureName, FeatureExecutionStatus.Success, "Success");
            LoggingService.LogInfo($"[FeatureExecutionEngine] Feature '{featureName}' finished successfully.");
            return result;
        }
        catch (OperationCanceledException oce)
        {
            UpdateState(featureName, FeatureExecutionStatus.Warning, $"Canceled: {oce.Message}");
            LoggingService.LogError($"[FeatureExecutionEngine] Feature '{featureName}' canceled: {oce.Message}");
            throw;
        }
        catch (Exception ex)
        {
            UpdateState(featureName, FeatureExecutionStatus.Error, ex.Message);
            LoggingService.LogException(ex, $"FeatureExecutionEngine.{featureName}");
            throw;
        }
    }
}

internal sealed class CircularBuffer<T>(int capacity)
{
    private readonly T[] _buf = capacity > 0
        ? new T[capacity]
        : throw new ArgumentOutOfRangeException(nameof(capacity));
    private int _head;
    private int _count;

    public int Capacity { get; } = capacity;
    public int Count => _count;

    public void Add(T item)
    {
        _buf[_head] = item;
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    public List<T> ToListNewestFirst()
    {
        var result = new List<T>(_count);
        for (int i = 1; i <= _count; i++)
        {
            int idx = (_head - i + Capacity) % Capacity;
            result.Add(_buf[idx]);
        }
        return result;
    }
}
