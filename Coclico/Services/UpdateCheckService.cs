#nullable enable
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace Coclico.Services;

public class UpdateCheckService
{
    private static UpdateCheckService? _instance;
    private static readonly object _lockObject = new();

    private readonly UpdateManager _updateManager;
    private readonly ILogger<UpdateCheckService> _logger;
    private readonly SettingsService _settingsService;
    private System.Timers.Timer? _updateCheckTimer;
    private readonly string _currentVersion =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0]
            ?? "1.0.0";
    private bool _isRunning = false;

    public event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;

    public event EventHandler<Exception>? CheckFailed;

    public static UpdateCheckService GetInstance(UpdateManager updateManager, ILogger<UpdateCheckService> logger, SettingsService settingsService)
    {
        if (_instance == null)
        {
            lock (_lockObject)
            {
                _instance ??= new UpdateCheckService(updateManager, logger, settingsService);
            }
        }
        return _instance;
    }

    private UpdateCheckService(UpdateManager updateManager, ILogger<UpdateCheckService> logger, SettingsService settingsService)
    {
        _updateManager = updateManager ?? throw new ArgumentNullException(nameof(updateManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public void Start()
    {
        if (_isRunning)
        {
            _logger.LogWarning("UpdateCheckService is already running");
            return;
        }

        _logger.LogInformation("Starting UpdateCheckService with 5-minute interval");

        _ = CheckForUpdatesAsync();

        _updateCheckTimer = new System.Timers.Timer(TimeSpan.FromMinutes(5).TotalMilliseconds);
        _updateCheckTimer.Elapsed += async (s, e) => await OnTimerElapsed(s, e);
        _updateCheckTimer.AutoReset = true;
        _updateCheckTimer.Start();

        _isRunning = true;
    }

    public void Stop()
    {
        if (_updateCheckTimer != null)
        {
            _updateCheckTimer.Stop();
            _updateCheckTimer.Dispose();
            _updateCheckTimer = null;
        }
        _isRunning = false;
        _logger.LogInformation("UpdateCheckService stopped");
    }

    public async Task<GitHubRelease?> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        _logger.LogInformation($"Checking for updates (current version: {_currentVersion})");

        try
        {
            var release = await _updateManager.CheckForUpdatesAsync(_currentVersion, ct);

            if (release != null)
            {
                _logger.LogInformation($"Update available: {release.TagName}");
                UpdateAvailable?.Invoke(this, new UpdateAvailableEventArgs(release));
            }
            else
            {
                _logger.LogInformation("Already up-to-date");
            }

            return release;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to check for updates: {ex.Message}");
            CheckFailed?.Invoke(this, ex);
            return null;
        }
    }

    private async Task OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        await CheckForUpdatesAsync();
    }

    public bool IsRunning => _isRunning;

    public string CurrentVersion => _currentVersion;
}

public class UpdateAvailableEventArgs(GitHubRelease release) : EventArgs
{
    public GitHubRelease Release { get; } = release;
}
