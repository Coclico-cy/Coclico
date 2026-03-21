using System;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace Coclico.Services
{
    /// <summary>
    /// Service for periodic update checking every 5 minutes.
    /// Implements singleton pattern for app-wide update monitoring.
    /// </summary>
    public class UpdateCheckService
    {
        private static UpdateCheckService? _instance;
        private static readonly object _lockObject = new();

        private readonly UpdateManager _updateManager;
        private readonly ILogger<UpdateCheckService> _logger;
        private readonly SettingsService _settingsService;
        private System.Timers.Timer? _updateCheckTimer;
        private readonly string _currentVersion = "1.0.3"; // TODO: Get from Assembly version
        private bool _isRunning = false;

        /// <summary>
        /// Event raised when an update is found
        /// </summary>
        public event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;

        /// <summary>
        /// Event raised when update check fails
        /// </summary>
        public event EventHandler<Exception>? CheckFailed;

        public static UpdateCheckService GetInstance(UpdateManager updateManager, ILogger<UpdateCheckService> logger, SettingsService settingsService)
        {
            if (_instance == null)
            {
                lock (_lockObject)
                {
                    if (_instance == null)
                    {
                        _instance = new UpdateCheckService(updateManager, logger, settingsService);
                    }
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

        /// <summary>
        /// Start periodic update checking (every 5 minutes)
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                _logger.LogWarning("UpdateCheckService is already running");
                return;
            }

            _logger.LogInformation("Starting UpdateCheckService with 5-minute interval");

            // First check immediately
            _ = CheckForUpdatesAsync();

            // Then set up timer for every 5 minutes (300000 ms)
            _updateCheckTimer = new System.Timers.Timer(TimeSpan.FromMinutes(5).TotalMilliseconds);
            _updateCheckTimer.Elapsed += async (s, e) => await OnTimerElapsed(s, e);
            _updateCheckTimer.AutoReset = true;
            _updateCheckTimer.Start();

            _isRunning = true;
        }

        /// <summary>
        /// Stop periodic update checking
        /// </summary>
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

        /// <summary>
        /// Manually trigger an update check
        /// </summary>
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

    /// <summary>
    /// Event arguments for update available notifications
    /// </summary>
    public class UpdateAvailableEventArgs : EventArgs
    {
        public GitHubRelease Release { get; }

        public UpdateAvailableEventArgs(GitHubRelease release)
        {
            Release = release;
        }
    }
}
