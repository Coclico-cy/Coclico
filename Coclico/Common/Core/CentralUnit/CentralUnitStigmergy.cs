using System.IO;
using System.Text.Json;

namespace Coclico.Services;

public sealed class CentralStigmergyModuleSnapshot
{
    public required string Module { get; init; }

    public required double Score { get; init; }

    public required long Uses { get; init; }
}

public sealed class CentralUnitStigmergy
{
    public static readonly TimeSpan DefaultTransitionHalfLife = TimeSpan.FromHours(6);
    public static readonly TimeSpan DefaultHourlyHalfLife = TimeSpan.FromDays(3);

    private static readonly string DefaultPersistencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico", "stigmergy.json");

    public static CentralUnitStigmergy Current { get; } = new(persistencePath: DefaultPersistencePath);

    private static readonly TimeSpan MinSaveInterval = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions PersistJsonOptions = new() { WriteIndented = false };

    private const int MaxModules = 64;
    private const int MaxTransitions = 512;
    private const int MaxModulesPerHour = 32;
    private const int MaxTtlKeys = 128;
    private const double MinTtlFactor = 0.25;
    private const double MaxTtlFactor = 4.0;
    private const int FreshStreakToExtend = 4;
    private const int ExpiredStreakToShorten = 2;
    private const double EpsilonScore = 0.0001;

    private readonly object _stateLock = new();
    private readonly TimeSpan _transitionHalfLife;
    private readonly TimeSpan _hourlyHalfLife;
    private readonly Dictionary<string, ModuleState> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, TransitionState>> _transitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, Dictionary<string, HourEntry>> _hourlyUsage = new();
    private readonly Dictionary<string, TtlState> _ttlStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _saveLock = new();
    private readonly string? _persistencePath;
    private string? _lastModule;
    private bool _dirty;
    private DateTime _lastSaveUtc;

    public CentralUnitStigmergy(TimeSpan? transitionHalfLife = null, TimeSpan? hourlyHalfLife = null, string? persistencePath = null)
    {
        _transitionHalfLife = transitionHalfLife ?? DefaultTransitionHalfLife;
        _hourlyHalfLife = hourlyHalfLife ?? DefaultHourlyHalfLife;
        _persistencePath = persistencePath;
        if (_persistencePath != null)
        {
            LoadState();
        }
    }

    public string? LastModule
    {
        get
        {
            lock (_stateLock)
            {
                return _lastModule;
            }
        }
    }

    public void RecordTransition(string? fromModule, string toModule)
    {
        string to = NormalizeModule(toModule);
        if (to.Length == 0)
        {
            return;
        }

        string? from = string.IsNullOrWhiteSpace(fromModule) ? null : NormalizeModule(fromModule);

        lock (_stateLock)
        {
            DateTime now = DateTime.UtcNow;

            if (from != null && !string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                TouchModuleLocked(from, now);
                RecordTransitionLocked(from, to, now);
            }

            TouchModuleLocked(to, now);
            RecordHourLocked(to, DateTime.Now.Hour);
            _lastModule = to;
            _dirty = true;
        }

        MaybeSave();
    }

    public void RecordModuleUsage(string module)
    {
        string normalized = NormalizeModule(module);
        if (normalized.Length == 0)
        {
            return;
        }

        lock (_stateLock)
        {
            DateTime now = DateTime.UtcNow;
            TouchModuleLocked(normalized, now);
            RecordHourLocked(normalized, DateTime.Now.Hour);
            _lastModule = normalized;
            _dirty = true;
        }

        MaybeSave();
    }

    public List<(string Module, double Probability)> GetPredictedNextModules(string currentModule, int max = 3)
    {
        var result = new List<(string Module, double Probability)>();
        string current = NormalizeModule(currentModule);
        if (current.Length == 0)
        {
            return result;
        }

        lock (_stateLock)
        {
            if (!_transitions.TryGetValue(current, out Dictionary<string, TransitionState>? targets) || targets is null)
            {
                return result;
            }

            DateTime now = DateTime.UtcNow;
            var scores = new List<(string Module, double Score)>();
            double total = 0;
            foreach (KeyValuePair<string, TransitionState> entry in targets)
            {
                double score = DecayedScore(entry.Value.Score, entry.Value.LastReinforcedUtc, now, _transitionHalfLife);
                if (score <= EpsilonScore)
                {
                    continue;
                }

                scores.Add((entry.Key, score));
                total += score;
            }

            foreach ((string module, double score) in scores.OrderByDescending(item => item.Score).Take(Math.Max(1, max)))
            {
                result.Add((module, total > 0 ? score / total : 0));
            }

            return result;
        }
    }

    public List<(string Module, double Score)> GetLikelyModulesAtHour(int hour, int max = 3)
    {
        var result = new List<(string Module, double Score)>();
        if (hour < 0 || hour > 23)
        {
            return result;
        }

        lock (_stateLock)
        {
            if (!_hourlyUsage.TryGetValue(hour, out Dictionary<string, HourEntry>? entries) || entries is null)
            {
                return result;
            }

            DateTime now = DateTime.UtcNow;
            foreach (KeyValuePair<string, HourEntry> entry in entries)
            {
                double score = DecayedScore(entry.Value.Score, entry.Value.LastReinforcedUtc, now, _hourlyHalfLife);
                if (score > EpsilonScore)
                {
                    result.Add((entry.Key, score));
                }
            }

            return result
                .OrderByDescending(item => item.Score)
                .Take(Math.Max(1, max))
                .ToList();
        }
    }

    public void RecordCacheOutcome(string key, bool wasFresh)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (_stateLock)
        {
            if (!_ttlStates.TryGetValue(key, out TtlState? state) || state is null)
            {
                if (_ttlStates.Count >= MaxTtlKeys)
                {
                    TrimTtlLocked();
                }

                state = new TtlState();
                _ttlStates[key] = state;
            }

            if (wasFresh)
            {
                state.FreshCount++;
                state.ConsecutiveExpired = 0;
                state.ConsecutiveFresh++;
                if (state.ConsecutiveFresh >= FreshStreakToExtend)
                {
                    state.TtlFactor = Math.Min(MaxTtlFactor, state.TtlFactor * 2);
                    state.ConsecutiveFresh = 0;
                }
            }
            else
            {
                state.ExpiredCount++;
                state.ConsecutiveFresh = 0;
                state.ConsecutiveExpired++;
                if (state.ConsecutiveExpired >= ExpiredStreakToShorten)
                {
                    state.TtlFactor = Math.Max(MinTtlFactor, state.TtlFactor / 2);
                    state.ConsecutiveExpired = 0;
                }
            }

            _dirty = true;
        }

        MaybeSave();
    }

    public TimeSpan GetAdaptedTtl(string key, TimeSpan baseTtl)
    {
        if (string.IsNullOrWhiteSpace(key) || baseTtl <= TimeSpan.Zero)
        {
            return baseTtl;
        }

        lock (_stateLock)
        {
            return _ttlStates.TryGetValue(key, out TtlState? state) && state is not null
                ? TimeSpan.FromTicks((long)(baseTtl.Ticks * state.TtlFactor))
                : baseTtl;
        }
    }

    public List<CentralStigmergyModuleSnapshot> GetModuleSnapshot()
    {
        lock (_stateLock)
        {
            DateTime now = DateTime.UtcNow;
            return _modules.Values
                .Select(state => new CentralStigmergyModuleSnapshot
                {
                    Module = state.Name,
                    Score = Math.Round(DecayedScore(state.Score, state.LastReinforcedUtc, now, _transitionHalfLife), 3),
                    Uses = state.Uses
                })
                .OrderByDescending(snapshot => snapshot.Score)
                .ToList();
        }
    }

    public List<(string From, string To, double Score)> GetTransitionSnapshot()
    {
        lock (_stateLock)
        {
            DateTime now = DateTime.UtcNow;
            var result = new List<(string From, string To, double Score)>();
            foreach (KeyValuePair<string, Dictionary<string, TransitionState>> fromEntry in _transitions)
            {
                foreach (KeyValuePair<string, TransitionState> toEntry in fromEntry.Value)
                {
                    result.Add((fromEntry.Key, toEntry.Key,
                        Math.Round(DecayedScore(toEntry.Value.Score, toEntry.Value.LastReinforcedUtc, now, _transitionHalfLife), 3)));
                }
            }

            return result.OrderByDescending(item => item.Score).ToList();
        }
    }

    public List<(string Key, double Factor, int FreshCount, int ExpiredCount)> GetTtlSnapshot()
    {
        lock (_stateLock)
        {
            return _ttlStates
                .Select(entry => (entry.Key, Math.Round(entry.Value.TtlFactor, 3), entry.Value.FreshCount, entry.Value.ExpiredCount))
                .OrderByDescending(item => item.Item2)
                .ToList();
        }
    }

    public void Clear()
    {
        lock (_stateLock)
        {
            _modules.Clear();
            _transitions.Clear();
            _hourlyUsage.Clear();
            _ttlStates.Clear();
            _lastModule = null;
            _dirty = false;
        }

        TryDeletePersistenceFile();
    }

    public void SaveState()
    {
        if (_persistencePath == null)
        {
            return;
        }

        lock (_saveLock)
        {
            try
            {
                PersistedState snapshot;
                lock (_stateLock)
                {
                    snapshot = BuildPersistedSnapshotLocked();
                }

                string? dir = Path.GetDirectoryName(_persistencePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    _ = Directory.CreateDirectory(dir);
                }

                string tmpPath = _persistencePath + ".tmp";
                string json = JsonSerializer.Serialize(snapshot, PersistJsonOptions);
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, _persistencePath, overwrite: true);
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "CentralUnitStigmergy.SaveState");
            }
        }
    }

    private void MaybeSave()
    {
        if (_persistencePath == null)
        {
            return;
        }

        try
        {
            bool shouldSave = false;
            lock (_stateLock)
            {
                if (_dirty && DateTime.UtcNow - _lastSaveUtc >= MinSaveInterval)
                {
                    _dirty = false;
                    _lastSaveUtc = DateTime.UtcNow;
                    shouldSave = true;
                }
            }

            if (shouldSave)
            {
                SaveState();
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CentralUnitStigmergy.MaybeSave");
        }
    }

    private void LoadState()
    {
        if (_persistencePath == null)
        {
            return;
        }

        try
        {
            if (!File.Exists(_persistencePath))
            {
                return;
            }

            PersistedState? state = JsonSerializer.Deserialize<PersistedState>(File.ReadAllText(_persistencePath), PersistJsonOptions);
            if (state is null)
            {
                return;
            }

            lock (_stateLock)
            {
                ApplyPersistedStateLocked(state);
                _dirty = false;
                _lastSaveUtc = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CentralUnitStigmergy.LoadState");
        }
    }

    private PersistedState BuildPersistedSnapshotLocked()
    {
        return new PersistedState
        {
            LastModule = _lastModule,
            Modules = _modules.Values.Select(state => new PersistedModule
            {
                Name = state.Name,
                Score = state.Score,
                Uses = state.Uses,
                LastReinforcedUtc = state.LastReinforcedUtc
            }).ToList(),
            Transitions = _transitions
                .SelectMany(fromEntry => fromEntry.Value.Select(toEntry => new PersistedTransition
                {
                    From = fromEntry.Key,
                    To = toEntry.Key,
                    Score = toEntry.Value.Score,
                    LastReinforcedUtc = toEntry.Value.LastReinforcedUtc
                }))
                .ToList(),
            Hourly = _hourlyUsage
                .SelectMany(hourEntry => hourEntry.Value.Select(moduleEntry => new PersistedHourEntry
                {
                    Hour = hourEntry.Key,
                    Module = moduleEntry.Key,
                    Score = moduleEntry.Value.Score,
                    LastReinforcedUtc = moduleEntry.Value.LastReinforcedUtc
                }))
                .ToList(),
            Ttl = _ttlStates.Select(entry => new PersistedTtl
            {
                Key = entry.Key,
                TtlFactor = entry.Value.TtlFactor,
                FreshCount = entry.Value.FreshCount,
                ExpiredCount = entry.Value.ExpiredCount,
                ConsecutiveFresh = entry.Value.ConsecutiveFresh,
                ConsecutiveExpired = entry.Value.ConsecutiveExpired
            }).ToList()
        };
    }

    private void ApplyPersistedStateLocked(PersistedState state)
    {
        _modules.Clear();
        foreach (PersistedModule module in state.Modules)
        {
            if (string.IsNullOrWhiteSpace(module.Name) || _modules.Count >= MaxModules)
            {
                continue;
            }

            string name = NormalizeModule(module.Name);
            if (name.Length == 0 || _modules.ContainsKey(name))
            {
                continue;
            }

            _modules[name] = new ModuleState(name)
            {
                Score = Math.Max(0, module.Score),
                Uses = Math.Max(0, module.Uses),
                LastReinforcedUtc = module.LastReinforcedUtc
            };
        }

        _transitions.Clear();
        int transitionCount = 0;
        foreach (PersistedTransition transition in state.Transitions)
        {
            if (transitionCount >= MaxTransitions)
            {
                break;
            }

            string from = NormalizeModule(transition.From);
            string to = NormalizeModule(transition.To);
            if (from.Length == 0 || to.Length == 0)
            {
                continue;
            }

            if (!_transitions.TryGetValue(from, out Dictionary<string, TransitionState>? targets) || targets is null)
            {
                targets = new Dictionary<string, TransitionState>(StringComparer.OrdinalIgnoreCase);
                _transitions[from] = targets;
            }

            targets[to] = new TransitionState
            {
                Score = Math.Max(0, transition.Score),
                LastReinforcedUtc = transition.LastReinforcedUtc
            };
            transitionCount++;
        }

        _hourlyUsage.Clear();
        foreach (PersistedHourEntry hourEntry in state.Hourly)
        {
            if (hourEntry.Hour < 0 || hourEntry.Hour > 23 || string.IsNullOrWhiteSpace(hourEntry.Module))
            {
                continue;
            }

            if (!_hourlyUsage.TryGetValue(hourEntry.Hour, out Dictionary<string, HourEntry>? entries) || entries is null)
            {
                entries = new Dictionary<string, HourEntry>(StringComparer.OrdinalIgnoreCase);
                _hourlyUsage[hourEntry.Hour] = entries;
            }

            if (entries.Count >= MaxModulesPerHour)
            {
                continue;
            }

            string module = NormalizeModule(hourEntry.Module);
            if (module.Length == 0)
            {
                continue;
            }

            entries[module] = new HourEntry
            {
                Score = Math.Max(0, hourEntry.Score),
                LastReinforcedUtc = hourEntry.LastReinforcedUtc
            };
        }

        _ttlStates.Clear();
        foreach (PersistedTtl ttl in state.Ttl)
        {
            if (string.IsNullOrWhiteSpace(ttl.Key) || _ttlStates.Count >= MaxTtlKeys)
            {
                continue;
            }

            _ttlStates[ttl.Key] = new TtlState
            {
                TtlFactor = Math.Clamp(ttl.TtlFactor, MinTtlFactor, MaxTtlFactor),
                FreshCount = Math.Max(0, ttl.FreshCount),
                ExpiredCount = Math.Max(0, ttl.ExpiredCount),
                ConsecutiveFresh = Math.Max(0, ttl.ConsecutiveFresh),
                ConsecutiveExpired = Math.Max(0, ttl.ConsecutiveExpired)
            };
        }

        _lastModule = string.IsNullOrWhiteSpace(state.LastModule) ? null : NormalizeModule(state.LastModule);
    }

    private void TryDeletePersistenceFile()
    {
        if (_persistencePath == null)
        {
            return;
        }

        lock (_saveLock)
        {
            try
            {
                if (File.Exists(_persistencePath))
                {
                    File.Delete(_persistencePath);
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "CentralUnitStigmergy.Clear");
            }
        }
    }

    private static string NormalizeModule(string? module)
    {
        return module is null ? string.Empty : module.Trim().ToLowerInvariant();
    }

    private static double DecayedScore(double score, DateTime lastReinforcedUtc, DateTime now, TimeSpan halfLife)
    {
        if (halfLife <= TimeSpan.Zero || score <= 0)
        {
            return score;
        }

        double elapsedSeconds = Math.Max(0, (now - lastReinforcedUtc).TotalSeconds);
        return score * Math.Pow(0.5, elapsedSeconds / halfLife.TotalSeconds);
    }

    private void TouchModuleLocked(string module, DateTime now)
    {
        if (!_modules.TryGetValue(module, out ModuleState? state) || state is null)
        {
            if (_modules.Count >= MaxModules)
            {
                TrimModulesLocked(now);
            }

            state = new ModuleState(module);
            _modules[module] = state;
        }

        double decayed = DecayedScore(state.Score, state.LastReinforcedUtc, now, _transitionHalfLife);
        state.Score = decayed + 1;
        state.Uses++;
        state.LastReinforcedUtc = now;
    }

    private void RecordTransitionLocked(string from, string to, DateTime now)
    {
        if (!_transitions.TryGetValue(from, out Dictionary<string, TransitionState>? targets) || targets is null)
        {
            targets = new Dictionary<string, TransitionState>(StringComparer.OrdinalIgnoreCase);
            _transitions[from] = targets;
        }

        if (!targets.TryGetValue(to, out TransitionState? state) || state is null)
        {
            TrimTransitionsLocked(now);
            state = new TransitionState();
            targets[to] = state;
        }

        double decayed = DecayedScore(state.Score, state.LastReinforcedUtc, now, _transitionHalfLife);
        state.Score = decayed + 1;
        state.LastReinforcedUtc = now;
    }

    private void RecordHourLocked(string module, int hour)
    {
        if (!_hourlyUsage.TryGetValue(hour, out Dictionary<string, HourEntry>? entries) || entries is null)
        {
            entries = new Dictionary<string, HourEntry>(StringComparer.OrdinalIgnoreCase);
            _hourlyUsage[hour] = entries;
        }

        if (!entries.TryGetValue(module, out HourEntry? entry) || entry is null)
        {
            if (entries.Count >= MaxModulesPerHour)
            {
                TrimHourLocked(hour);
            }

            entry = new HourEntry();
            entries[module] = entry;
        }

        DateTime now = DateTime.UtcNow;
        double decayed = DecayedScore(entry.Score, entry.LastReinforcedUtc, now, _hourlyHalfLife);
        entry.Score = decayed + 1;
        entry.LastReinforcedUtc = now;
    }

    private void TrimModulesLocked(DateTime now)
    {
        string? weakest = null;
        double weakestScore = double.MaxValue;
        foreach (KeyValuePair<string, ModuleState> entry in _modules)
        {
            double score = DecayedScore(entry.Value.Score, entry.Value.LastReinforcedUtc, now, _transitionHalfLife);
            if (score < weakestScore)
            {
                weakestScore = score;
                weakest = entry.Key;
            }
        }

        if (weakest != null)
        {
            _ = _modules.Remove(weakest);
        }
    }

    private void TrimTransitionsLocked(DateTime now)
    {
        int total = 0;
        foreach (Dictionary<string, TransitionState> targets in _transitions.Values)
        {
            total += targets.Count;
        }

        while (total > MaxTransitions - 1)
        {
            string? minFrom = null;
            string? minTo = null;
            double minScore = double.MaxValue;
            foreach (KeyValuePair<string, Dictionary<string, TransitionState>> fromEntry in _transitions)
            {
                foreach (KeyValuePair<string, TransitionState> toEntry in fromEntry.Value)
                {
                    double score = DecayedScore(toEntry.Value.Score, toEntry.Value.LastReinforcedUtc, now, _transitionHalfLife);
                    if (score < minScore)
                    {
                        minScore = score;
                        minFrom = fromEntry.Key;
                        minTo = toEntry.Key;
                    }
                }
            }

            if (minFrom == null || minTo == null)
            {
                break;
            }

            _ = _transitions[minFrom].Remove(minTo);
            if (_transitions[minFrom].Count == 0)
            {
                _ = _transitions.Remove(minFrom);
            }

            total--;
        }
    }

    private void TrimHourLocked(int hour)
    {
        if (!_hourlyUsage.TryGetValue(hour, out Dictionary<string, HourEntry>? entries) || entries is null)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        string? weakest = null;
        double weakestScore = double.MaxValue;
        foreach (KeyValuePair<string, HourEntry> entry in entries)
        {
            double score = DecayedScore(entry.Value.Score, entry.Value.LastReinforcedUtc, now, _hourlyHalfLife);
            if (score < weakestScore)
            {
                weakestScore = score;
                weakest = entry.Key;
            }
        }

        if (weakest != null)
        {
            _ = entries.Remove(weakest);
        }
    }

    private void TrimTtlLocked()
    {
        string? leastActive = null;
        long leastActivity = long.MaxValue;
        foreach (KeyValuePair<string, TtlState> entry in _ttlStates)
        {
            long activity = (long)entry.Value.FreshCount + entry.Value.ExpiredCount;
            if (activity < leastActivity)
            {
                leastActivity = activity;
                leastActive = entry.Key;
            }
        }

        if (leastActive != null)
        {
            _ = _ttlStates.Remove(leastActive);
        }
    }

    private sealed class ModuleState(string name)
    {
        public string Name { get; } = name;

        public double Score { get; set; }

        public long Uses { get; set; }

        public DateTime LastReinforcedUtc { get; set; }
    }

    private sealed class TransitionState
    {
        public double Score { get; set; }

        public DateTime LastReinforcedUtc { get; set; }
    }

    private sealed class HourEntry
    {
        public double Score { get; set; }

        public DateTime LastReinforcedUtc { get; set; }
    }

    private sealed class TtlState
    {
        public double TtlFactor { get; set; } = 1.0;

        public int FreshCount { get; set; }

        public int ExpiredCount { get; set; }

        public int ConsecutiveFresh { get; set; }

        public int ConsecutiveExpired { get; set; }
    }

    private sealed class PersistedState
    {
        public string? LastModule { get; set; }

        public List<PersistedModule> Modules { get; set; } = [];

        public List<PersistedTransition> Transitions { get; set; } = [];

        public List<PersistedHourEntry> Hourly { get; set; } = [];

        public List<PersistedTtl> Ttl { get; set; } = [];
    }

    private sealed class PersistedModule
    {
        public string Name { get; set; } = string.Empty;

        public double Score { get; set; }

        public long Uses { get; set; }

        public DateTime LastReinforcedUtc { get; set; }
    }

    private sealed class PersistedTransition
    {
        public string From { get; set; } = string.Empty;

        public string To { get; set; } = string.Empty;

        public double Score { get; set; }

        public DateTime LastReinforcedUtc { get; set; }
    }

    private sealed class PersistedHourEntry
    {
        public int Hour { get; set; }

        public string Module { get; set; } = string.Empty;

        public double Score { get; set; }

        public DateTime LastReinforcedUtc { get; set; }
    }

    private sealed class PersistedTtl
    {
        public string Key { get; set; } = string.Empty;

        public double TtlFactor { get; set; } = 1.0;

        public int FreshCount { get; set; }

        public int ExpiredCount { get; set; }

        public int ConsecutiveFresh { get; set; }

        public int ConsecutiveExpired { get; set; }
    }
}
