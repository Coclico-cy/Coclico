using System.Reflection;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace Coclico.Services;

public class UpdateCheckService(
    UpdateManager updateManager,
    ILogger<UpdateCheckService> logger)
{
    private readonly UpdateManager _updateManager = updateManager ?? throw new ArgumentNullException(nameof(updateManager));
    private readonly ILogger<UpdateCheckService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private Timer? _updateCheckTimer;

    public event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;
    public event EventHandler<Exception>? CheckFailed;

    public bool IsRunning { get; private set; }

    public string CurrentVersion { get; } = GetCurrentVersion();

    public void Start()
    {
        if (IsRunning)
        {
            _logger.LogWarning("UpdateCheckService is already running");
            return;
        }

        _logger.LogInformation("Starting UpdateCheckService (startup check, then roughly every 6 hours)");

        _ = CheckForUpdatesAsync();

        TimeSpan interval = TimeSpan.FromHours(6) + TimeSpan.FromMinutes(Random.Shared.Next(0, 30));
        _updateCheckTimer = new Timer(interval.TotalMilliseconds)
        {
            AutoReset = true
        };
        _updateCheckTimer.Elapsed += (_, _) => _ = CheckForUpdatesAsync();
        _updateCheckTimer.Start();

        IsRunning = true;
    }

    public void Stop()
    {
        if (_updateCheckTimer != null)
        {
            _updateCheckTimer.Stop();
            _updateCheckTimer.Dispose();
            _updateCheckTimer = null;
        }

        IsRunning = false;
        _logger.LogInformation("UpdateCheckService stopped");
    }

    public async Task<GitHubRelease?> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        _logger.LogInformation($"Checking for updates (current version: {CurrentVersion})");

        try
        {
            GitHubRelease? release = await _updateManager.CheckForUpdatesAsync(CurrentVersion, ct).ConfigureAwait(false);

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

    public static string GetCurrentVersion()
    {
        return Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0]
            ?? "1.0.0";
    }
}

public class UpdateAvailableEventArgs(GitHubRelease release) : EventArgs
{
    public GitHubRelease Release { get; } = release;
}
