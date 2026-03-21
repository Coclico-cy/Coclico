using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Coclico.Services
{
    public enum FeatureExecutionStatus
    {
        Initializing,
        Running,
        Success,
        Warning,
        Error
    }

    public sealed class FeatureState : INotifyPropertyChanged
    {
        private FeatureExecutionStatus _status;
        private DateTime _timestampUtc;
        private string? _message;
        private DateTime _startTimeUtc;
        private DateTime? _endTimeUtc;

        public string FeatureName { get; }

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

        public ObservableCollection<string> LogHistory { get; } = new();

        public FeatureState(string featureName)
        {
            FeatureName = featureName ?? throw new ArgumentNullException(nameof(featureName));
            _status = FeatureExecutionStatus.Initializing;
            _timestampUtc = DateTime.UtcNow;
            _message = "Waiting";
            _startTimeUtc = DateTime.UtcNow;
            _endTimeUtc = null;
            LogHistory.Add("State initialized");
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        internal void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;
            LogHistory.Add($"[{DateTime.UtcNow:O}] {message}");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LogHistory)));
        }
    }

    public sealed class FeatureHealthChangedEventArgs : EventArgs
    {
        public FeatureState State { get; }

        public FeatureHealthChangedEventArgs(FeatureState state)
        {
            State = state;
        }
    }

    public sealed class FeatureExecutionContext
    {
        private readonly FeatureExecutionEngine _engine;
        private readonly string _featureName;

        internal FeatureExecutionContext(FeatureExecutionEngine engine, string featureName)
        {
            _engine = engine;
            _featureName = featureName;
        }

        public void Report(string message)
        {
            _engine.UpdateState(_featureName, FeatureExecutionStatus.Running, message);
        }

        public void SetStatus(FeatureExecutionStatus status, string? message = null)
        {
            _engine.UpdateState(_featureName, status, message);
        }
    }

    public sealed class FeatureExecutionEngine
    {
        private readonly ConcurrentDictionary<string, FeatureState> _stateMap =
            new(StringComparer.OrdinalIgnoreCase);

        public static FeatureExecutionEngine Instance { get; } = new();

        public event EventHandler<FeatureHealthChangedEventArgs>? HealthChanged;

        private FeatureExecutionEngine() { }

        internal void UpdateState(string featureName, FeatureExecutionStatus status, string? message = null)
        {
            if (string.IsNullOrWhiteSpace(featureName))
                throw new ArgumentException("Feature name cannot be null or whitespace.", nameof(featureName));

            var state = _stateMap.GetOrAdd(featureName, name => new FeatureState(name));
            var isInitialize = status == FeatureExecutionStatus.Initializing;

            if (isInitialize)
            {
                state.StartTimeUtc = DateTime.UtcNow;
                state.EndTimeUtc   = null;
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
}

