using System.IO;
using System.Reflection;
using System.Timers;
using Coclico.Security.Domain;
using Coclico.Security.Infrastructure;
using Timer = System.Timers.Timer;

namespace Coclico.Services;

public sealed class IntegrityRuntimeService : IDisposable
{
    private const string ManifestFileName = "integrity.json";
    private const string TrustPolicyResourceName = "Coclico.Security.trust-policy.json";

    private static readonly TimeSpan PeriodicInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan WatcherDebounce = TimeSpan.FromSeconds(5);

    private static IntegrityRuntimeService? _current;

    private readonly string _appDir;
    private readonly string _manifestPath;
    private readonly TrustPolicy _trustPolicy;
    private readonly UpdatePolicy _updatePolicy;
    private readonly SecurityJournal? _journal;
    private readonly HashSet<string> _variablePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer? _periodicTimer;
    private readonly FileSystemWatcher? _watcher;
    private readonly object _gate = new();
    private readonly string _appVersion;

    private DateTime _lastWatcherTriggerUtc = DateTime.MinValue;
    private bool _started;

    public IntegrityRuntimeService()
    {
        _appDir = AppContext.BaseDirectory;
        _manifestPath = Path.Join(_appDir, ManifestFileName);
        _appVersion = UpdateCheckService.GetCurrentVersion();
        TrustPolicyDocument document = LoadEmbeddedTrustPolicy();
        _trustPolicy = new TrustPolicy { Keys = document.Keys };
        _updatePolicy = document.UpdatePolicy;
        _variablePaths.Add("logs");
        _variablePaths.Add("cache");
        _variablePaths.Add("audit");
        _variablePaths.Add("errors");
        _variablePaths.Add("settings.json");
        _variablePaths.Add("runtimes/win-x64/native/cuda12");
        _variablePaths.Add("resource/model");
        _variablePaths.Add("Uninstall");

        _journal = CreateJournal();

        _periodicTimer = new Timer(PeriodicInterval.TotalMilliseconds)
        {
            AutoReset = true,
            Enabled = false
        };
        _periodicTimer.Elapsed += OnPeriodicTimerElapsed;

        try
        {
            _watcher = new FileSystemWatcher(_appDir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = false
            };
            _watcher.Changed += OnWatcherSignal;
            _watcher.Created += OnWatcherSignal;
            _watcher.Deleted += OnWatcherSignal;
            _watcher.Renamed += OnWatcherSignal;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            LoggingService.LogWarning($"Integrity watcher unavailable: {ex.Message}");
            _watcher = null;
        }
    }

    public static IntegrityRuntimeService? Current => _current;

    private static void SetCurrent(IntegrityRuntimeService? service)
    {
        _current = service;
    }

    public IntegrityStatus Status { get; private set; } = IntegrityStatus.Unknown;

    public VerificationResult? LastResult { get; private set; }

    public UpdatePolicy UpdatePolicy => _updatePolicy;

    public event EventHandler<VerificationResult>? StatusChanged;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        SetCurrent(this);
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = true;
        }

        if (_periodicTimer != null)
        {
            _periodicTimer.Enabled = true;
        }

        _ = Task.Run(VerifyNow);
    }

    public VerificationResult VerifyNow()
    {
        VerificationResult result = IntegrityVerifier.VerifyDirectory(_appDir, _manifestPath, _trustPolicy, _variablePaths);
        ApplyResult(result);
        return result;
    }

    public bool IsSensitiveOperationAllowed(string operationName, out string reason)
    {
        reason = string.Empty;
        if (Status is IntegrityStatus.Altered or IntegrityStatus.RecoveryRequired)
        {
            reason = $"integrity status is {Status}: '{operationName}' is blocked";
            LoggingService.LogError($"[Integrity] Sensitive operation blocked: {reason}");
            return false;
        }

        if (Status == IntegrityStatus.Unknown)
        {
            LoggingService.LogWarning($"[Integrity] '{operationName}' allowed but application integrity could not be verified");
        }

        return true;
    }

    private void ApplyResult(VerificationResult result)
    {
        lock (_gate)
        {
            bool changed = LastResult is null || LastResult.Status != result.Status || LastResult.Incident != result.Incident;
            LastResult = result;
            Status = result.Status;
            if (changed)
            {
                LoggingService.LogInfo($"[Integrity] Status {result.Describe()}");
                _journal?.Append(DateTime.UtcNow, _appVersion, "StatusChanged", "Integrity", result.Status.ToString(), result.Incident.ToString(), result.Reason);
                StatusChanged?.Invoke(this, result);
            }
            else
            {
                _journal?.Append(DateTime.UtcNow, _appVersion, "Check", "Integrity", result.Status.ToString(), result.Incident.ToString(), result.Reason);
            }
        }
    }

    private void OnPeriodicTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        _ = Task.Run(VerifyNow);
    }

    private void OnWatcherSignal(object sender, FileSystemEventArgs e)
    {
        string extension = Path.GetExtension(e.Name ?? string.Empty);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".config", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (now - _lastWatcherTriggerUtc < WatcherDebounce)
        {
            return;
        }

        _lastWatcherTriggerUtc = now;
        _ = Task.Run(VerifyNow);
    }

    private SecurityJournal? CreateJournal()
    {
        try
        {
            string securityDir = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Coclico",
                "security");
            var keyStore = new DpapiJournalKeyStore();
            byte[]? key = keyStore.LoadOrCreateKey(Path.Join(securityDir, "journal.key"));
            if (key == null)
            {
                LoggingService.LogWarning("[Integrity] Journal key store unavailable on this platform, journal disabled");
                return null;
            }

            return new SecurityJournal(Path.Join(securityDir, "journal.log"), key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            LoggingService.LogWarning($"[Integrity] Journal unavailable: {ex.Message}");
            return null;
        }
    }

    private static TrustPolicyDocument LoadEmbeddedTrustPolicy()
    {
        try
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using Stream? stream = assembly.GetManifestResourceStream(TrustPolicyResourceName);
            if (stream != null)
            {
                return TrustStore.Load(stream);
            }

            LoggingService.LogWarning($"[Integrity] Embedded resource '{TrustPolicyResourceName}' not found");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Text.Json.JsonException)
        {
            LoggingService.LogWarning($"[Integrity] Trust policy could not be loaded: {ex.Message}");
        }

        return TrustStore.Empty();
    }

    public void Dispose()
    {
        if (_periodicTimer != null)
        {
            _periodicTimer.Enabled = false;
            _periodicTimer.Elapsed -= OnPeriodicTimerElapsed;
            _periodicTimer.Dispose();
        }

        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnWatcherSignal;
            _watcher.Created -= OnWatcherSignal;
            _watcher.Deleted -= OnWatcherSignal;
            _watcher.Renamed -= OnWatcherSignal;
            _watcher.Dispose();
        }

        if (ReferenceEquals(_current, this))
        {
            SetCurrent(null);
        }
    }
}

public static class IntegrityGate
{
    public static bool IsSensitiveOperationAllowed(string operationName, out string reason)
    {
        IntegrityRuntimeService? service = IntegrityRuntimeService.Current;
        if (service == null)
        {
            reason = string.Empty;
            return true;
        }

        return service.IsSensitiveOperationAllowed(operationName, out reason);
    }
}
