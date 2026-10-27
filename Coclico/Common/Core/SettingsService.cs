using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Coclico.Services;

public class AppSettings
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = "fr";

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#39C6C0";

    [JsonPropertyName("themePreset")]
    public string ThemePreset { get; set; } = "Cyan";

    [JsonPropertyName("backgroundMode")]
    public string BackgroundMode { get; set; } = "Dark";

    [JsonPropertyName("backgroundOpacity")]
    public double BackgroundOpacity { get; set; } = 1.0;

    [JsonPropertyName("cardOpacity")]
    public double CardOpacity { get; set; } = 0.07;

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 13.0;

    [JsonPropertyName("wingetScope")]
    public string WingetScope { get; set; } = "machine";

    [JsonPropertyName("launchAtStartup")]
    public bool LaunchAtStartup { get; set; }

    [JsonPropertyName("minimizeToTray")]
    public bool MinimizeToTray { get; set; }

    [JsonPropertyName("firstRun")]
    public bool FirstRun { get; set; } = true;

    [JsonPropertyName("skipUninstallConfirmation")]
    public bool SkipUninstallConfirmation { get; set; }

    [JsonPropertyName("hasSeenWelcome")]
    public bool HasSeenWelcome { get; set; }

    [JsonPropertyName("hasSeenCoclico20Welcome")]
    public bool HasSeenCoclico20Welcome { get; set; }

    [JsonPropertyName("launchMode")]
    public string LaunchMode { get; set; } = "Normal";

    [JsonPropertyName("startMaximized")]
    public bool StartMaximized { get; set; } = true;

    [JsonPropertyName("auditRetentionDays")]
    public int AuditRetentionDays { get; set; } = 90;

    [JsonPropertyName("diagnosticEnableHardwareSensors")]
    public bool DiagnosticEnableHardwareSensors { get; set; } = true;

    [JsonPropertyName("diagnosticEnableLiveMonitoring")]
    public bool DiagnosticEnableLiveMonitoring { get; set; }

    [JsonPropertyName("diagnosticHistoryRetentionCount")]
    public int DiagnosticHistoryRetentionCount { get; set; } = 30;

    [JsonPropertyName("diagnosticHistoryRetentionDays")]
    public int DiagnosticHistoryRetentionDays { get; set; } = 30;

    [JsonPropertyName("diagnosticNetworkProbeTimeoutMs")]
    public int DiagnosticNetworkProbeTimeoutMs { get; set; } = 2000;

    [JsonPropertyName("diagnosticMaxParallelChecks")]
    public int DiagnosticMaxParallelChecks { get; set; } = 3;

    [JsonPropertyName("diagnosticEnableExternalBandwidthTest")]
    public bool DiagnosticEnableExternalBandwidthTest { get; set; }

    [JsonPropertyName("aiIdleTimeoutMinutes")]
    public int AiIdleTimeoutMinutes { get; set; } = 5;

    [JsonPropertyName("aiProvider")]
    public string AiProvider { get; set; } = "LocalGGUF";

    [JsonPropertyName("aiModel")]
    public string AiModel { get; set; } = "IA-support-chat.gguf";

    [JsonPropertyName("ollamaEndpoint")]
    public string OllamaEndpoint { get; set; } =
        new UriBuilder(Uri.UriSchemeHttp, IPAddress.Loopback.ToString(), 11434).Uri.ToString().TrimEnd('/');

    [JsonPropertyName("aiEnableFloatingButton")]
    public bool AiEnableFloatingButton { get; set; } = true;

    [JsonPropertyName("aiEnabled")]
    public bool AiEnabled { get; set; } = true;

    [JsonPropertyName("aiUseGpu")]
    public bool AiUseGpu { get; set; } = true;

    [JsonPropertyName("aiGpuLayers")]
    public int AiGpuLayers { get; set; } = 32;

    [JsonPropertyName("autoCleanEnabled")]
    public bool AutoCleanEnabled { get; set; }

    [JsonPropertyName("autoCleanMode")]
    public string AutoCleanMode { get; set; } = "Interval";

    [JsonPropertyName("autoCleanIntervalMinutes")]
    public int AutoCleanIntervalMinutes { get; set; } = 15;

    [JsonPropertyName("autoCleanThresholdPercent")]
    public int AutoCleanThresholdPercent { get; set; } = 80;

    [JsonPropertyName("autoCleanProtectForeground")]
    public bool AutoCleanProtectForeground { get; set; } = true;

    [JsonPropertyName("autoCleanProfile")]
    public string AutoCleanProfile { get; set; } = "Smart";

    [JsonPropertyName("menuExpandedGroups")]
    public string MenuExpandedGroups { get; set; } = "Maintenance,Performance,Applications";

    [JsonPropertyName("adaptivePowerEnabled")]
    public bool AdaptivePowerEnabled { get; set; }

    [JsonPropertyName("powerPreferredMode")]
    public string PowerPreferredMode { get; set; } = "Optimal";

    [JsonPropertyName("skippedUpdateVersion")]
    public string SkippedUpdateVersion { get; set; } = "";
}

public class SettingsService
{
    public static class ThemeOpacityBounds
    {
        public const double BackgroundMin = 0.20;
        public const double BackgroundMax = 1.0;
        public const double CardMin = 0.01;
        public const double CardMax = 0.40;
    }

    public static readonly string SettingsDir = Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico");

    private static readonly string SettingsPath = Path.Join(SettingsDir, "settings.json");
    private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };
    private static readonly SemaphoreSlim _ioLock = new(1, 1);
    private static readonly string[] ValidLanguages = ["en", "fr"];
    private static readonly Regex AccentColorRegex = new("^#[0-9A-Fa-f]{6,8}$", RegexOptions.Compiled);

    public event Action<bool>? FloatingButtonVisibilityChanged;

    public AppSettings Settings { get; private set; } = new();

    public void NotifyFloatingButtonVisibilityChanged(bool visible)
    {
        FloatingButtonVisibilityChanged?.Invoke(visible);
    }

    public static string GetUserDataDirectory()
    {
        try
        {
            _ = Directory.CreateDirectory(SettingsDir);
            _ = Directory.CreateDirectory(Path.Join(SettingsDir, "profiles"));
            _ = Directory.CreateDirectory(Path.Join(SettingsDir, "avatars"));
            _ = Directory.CreateDirectory(Path.Join(SettingsDir, "logs"));
            _ = Directory.CreateDirectory(Path.Join(SettingsDir, "cache"));
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            LoggingService.LogException(ex, "SettingsService.GetUserDataDirectory");
        }
        return SettingsDir;
    }

    public static void OpenUserDataDirectory()
    {
        try
        {
            string dir = GetUserDataDirectory();
            _ = Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.InvalidOperationException)
        {
            LoggingService.LogException(ex, "SettingsService.OpenUserDataDirectory");
        }
    }

    public async Task LoadAsync()
    {
        await LoadCoreAsync().ConfigureAwait(false);
    }

    public void Load()
    {
        _ioLock.Wait();
        bool modified = false;
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                AppSettings loaded = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                Settings = Sanitize(loaded, out modified);
            }
            else
            {
                Settings = new AppSettings();
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Text.Json.JsonException or System.FormatException)
        {
            Settings = new AppSettings();
            LoggingService.LogException(ex, "SettingsService.Load");
        }
        finally
        {
            _ = _ioLock.Release();
        }

        if (modified)
        {
            Save();
        }
    }

    public Task SaveAsync()
    {
        return SaveCoreAsync();
    }

    public void Save()
    {
        _ = Task.Run(SaveCoreAsync);
    }

    public void ApplyFrom(AppSettings source)
    {
        string json = JsonSerializer.Serialize(source, _writeOptions);
        AppSettings copy = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        Settings = Sanitize(copy, out _);
        Save();
    }

    public async Task<bool> ExportToAsync(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return false;
        }

        try
        {
            string? dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir))
            {
                _ = Directory.CreateDirectory(dir);
            }

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                string json = JsonSerializer.Serialize(Settings, _writeOptions);
                await File.WriteAllTextAsync(destinationPath, json).ConfigureAwait(false);
            }
            finally
            {
                _ = _ioLock.Release();
            }

            LoggingService.LogInfo($"[SettingsService] Réglages exportés vers « {destinationPath} »");
            return true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Security.SecurityException)
        {
            LoggingService.LogException(ex, "SettingsService.ExportTo");
            return false;
        }
    }

    public async Task<bool> ImportFromAsync(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return false;
        }

        try
        {
            string json = await File.ReadAllTextAsync(sourcePath).ConfigureAwait(false);
            AppSettings imported = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            AppSettings copy = Sanitize(imported, out _);

            copy.FirstRun = Settings.FirstRun;
            copy.HasSeenWelcome = Settings.HasSeenWelcome;
            copy.HasSeenCoclico20Welcome = Settings.HasSeenCoclico20Welcome;
            copy.SkippedUpdateVersion = Settings.SkippedUpdateVersion;

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Settings = copy;
            }
            finally
            {
                _ = _ioLock.Release();
            }

            await SaveCoreAsync().ConfigureAwait(false);
            LoggingService.LogInfo($"[SettingsService] Réglages importés depuis « {sourcePath} »");
            return true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Text.Json.JsonException or System.FormatException)
        {
            LoggingService.LogException(ex, "SettingsService.ImportFrom");
            return false;
        }
    }

    public void EnableAutostart()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser
                .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null)
            {
                return;
            }

            string exePath = Process.GetCurrentProcess().MainModule?.FileName
                ?? Path.Join(AppContext.BaseDirectory, "Coclico.exe");

            key.SetValue("Coclico", '"' + exePath + '"');
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception or System.InvalidOperationException)
        {
            LoggingService.LogException(ex, "SettingsService.EnableAutostart");
        }
    }

    public void DisableAutostart()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser
                .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue("Coclico", throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or System.IO.IOException or System.UnauthorizedAccessException)
        {
            LoggingService.LogException(ex, "SettingsService.DisableAutostart");
        }
    }

    private async Task LoadCoreAsync()
    {
        await _ioLock.WaitAsync().ConfigureAwait(false);
        bool modified = false;
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = await File.ReadAllTextAsync(SettingsPath).ConfigureAwait(false);
                AppSettings loaded = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                Settings = Sanitize(loaded, out modified);
            }
            else
            {
                Settings = new AppSettings();
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Text.Json.JsonException or System.FormatException)
        {
            Settings = new AppSettings();
            LoggingService.LogException(ex, "SettingsService.Load");
        }
        finally
        {
            _ = _ioLock.Release();
        }

        if (modified)
        {
            await SaveCoreAsync().ConfigureAwait(false);
        }
    }

    private async Task SaveCoreAsync()
    {
        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _ = Directory.CreateDirectory(SettingsDir);
            string json = JsonSerializer.Serialize(Settings, _writeOptions);
            string tempPath = SettingsPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            File.Move(tempPath, SettingsPath, overwrite: true);
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Text.Json.JsonException or System.FormatException)
        {
            LoggingService.LogException(ex, "SettingsService.Save");
        }
        finally
        {
            _ = _ioLock.Release();
        }
    }

    private static AppSettings Sanitize(AppSettings s, out bool modified)
    {
        modified = false;
        bool isModified = false;

        void FixDouble(double value, double min, double max, double epsilon, Action<double> set)
        {
            double clamped = Math.Clamp(value, min, max);
            if (Math.Abs(value - clamped) > epsilon)
            {
                set(clamped);
                isModified = true;
            }
        }

        void FixInt(int value, int min, int max, Action<int> set)
        {
            int clamped = Math.Clamp(value, min, max);
            if (clamped != value)
            {
                set(clamped);
                isModified = true;
            }
        }

        FixDouble(s.FontSize, 8.0, 32.0, 0.01, v => s.FontSize = v);
        FixDouble(s.CardOpacity, ThemeOpacityBounds.CardMin, ThemeOpacityBounds.CardMax, 0.001, v => s.CardOpacity = v);
        FixDouble(s.BackgroundOpacity, ThemeOpacityBounds.BackgroundMin, ThemeOpacityBounds.BackgroundMax, 0.001, v => s.BackgroundOpacity = v);
        FixInt(s.AiIdleTimeoutMinutes, 0, 1440, v => s.AiIdleTimeoutMinutes = v);
        FixInt(s.AiGpuLayers, 0, 128, v => s.AiGpuLayers = v);
        FixInt(s.AutoCleanIntervalMinutes, 1, 1440, v => s.AutoCleanIntervalMinutes = v);
        FixInt(s.AutoCleanThresholdPercent, 10, 99, v => s.AutoCleanThresholdPercent = v);

        if (Array.IndexOf(ValidLanguages, s.Language) < 0)
        {
            s.Language = "fr";
            modified = true;
        }

        if (string.IsNullOrWhiteSpace(s.AccentColor) || !AccentColorRegex.IsMatch(s.AccentColor))
        {
            s.AccentColor = "#39C6C0";
            modified = true;
        }

        if (string.Equals(s.ThemePreset, "Indigo", StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.AccentColor, "#6366F1", StringComparison.OrdinalIgnoreCase))
        {
            s.ThemePreset = "Cyan";
            s.AccentColor = "#39C6C0";
            modified = true;
        }

        if (s.WingetScope is not "machine" and not "user")
        {
            s.WingetScope = "machine";
            modified = true;
        }

        if (!LaunchModes.IsValid(s.LaunchMode))
        {
            s.LaunchMode = "Normal";
            modified = true;
        }

        if (s.AutoCleanMode is not "Interval" and not "Threshold" and not "Hybrid")
        {
            s.AutoCleanMode = "Interval";
            modified = true;
        }

        modified |= isModified;
        return s;
    }
}
