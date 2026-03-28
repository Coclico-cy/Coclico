#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Coclico.Services;

public class AppSettings
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = "fr";

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#6366F1";

    [JsonPropertyName("themePreset")]
    public string ThemePreset { get; set; } = "Indigo";

    [JsonPropertyName("backgroundMode")]
    public string BackgroundMode { get; set; } = "UltraDark";

    [JsonPropertyName("cardOpacity")]
    public double CardOpacity { get; set; } = 0.07;

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 13.0;

    [JsonPropertyName("sidebarWidth")]
    public double SidebarWidth { get; set; } = 220;

    [JsonPropertyName("compactMode")]
    public bool CompactMode { get; set; } = false;

    [JsonPropertyName("wingetScope")]
    public string WingetScope { get; set; } = "machine";

    [JsonPropertyName("launchAtStartup")]
    public bool LaunchAtStartup { get; set; } = false;

    [JsonPropertyName("minimizeToTray")]
    public bool MinimizeToTray { get; set; } = false;

    [JsonPropertyName("firstRun")]
    public bool FirstRun { get; set; } = true;

    [JsonPropertyName("launchMode")]
    public string LaunchMode { get; set; } = "Normal";

    [JsonPropertyName("autoPatcherAuditOnly")]
    public bool CodePatcherAuditOnly { get; set; } = true;

    [JsonPropertyName("auditRetentionDays")]
    public int AuditRetentionDays { get; set; } = 90;

    [JsonPropertyName("aiIdleTimeoutMinutes")]
    public int AiIdleTimeoutMinutes { get; set; } = 5;
}

public class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public AppSettings Settings { get; private set; } = new();

    public SettingsService()
    {
    }

    public async Task LoadAsync()
    {
        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = await File.ReadAllTextAsync(SettingsPath).ConfigureAwait(false);
                Settings = Sanitize(JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings());
            }
        }
        catch (Exception ex)
        {
            Settings = new AppSettings();
            LoggingService.LogException(ex, "SettingsService.LoadAsync");
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private static AppSettings Sanitize(AppSettings s)
    {
        s.FontSize = Math.Clamp(s.FontSize, 8.0, 32.0);
        s.SidebarWidth = Math.Clamp(s.SidebarWidth, 52.0, 500.0);
        s.CardOpacity = Math.Clamp(s.CardOpacity, 0.0, 1.0);

        string[] validLangs = ["en", "fr", "de", "es", "it", "ja", "ko", "pt", "ru", "zh"];
        if (!Array.Exists(validLangs, l => l == s.Language))
            s.Language = "fr";

        if (string.IsNullOrWhiteSpace(s.AccentColor) || !Regex.IsMatch(s.AccentColor, @"^#[0-9A-Fa-f]{6,8}$"))
            s.AccentColor = "#6366F1";

        if (s.WingetScope is not "machine" and not "user")
            s.WingetScope = "machine";

        s.AiIdleTimeoutMinutes = Math.Clamp(s.AiIdleTimeoutMinutes, 0, 1440);

        return s;
    }

    public async Task SaveAsync()
    {
        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(Settings, _writeOptions);
            await File.WriteAllTextAsync(SettingsPath, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "SettingsService.SaveAsync");
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public void Save()
    {
        _ = Task.Run(async () =>
        {
            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var json = JsonSerializer.Serialize(Settings, _writeOptions);
                await File.WriteAllTextAsync(SettingsPath, json).ConfigureAwait(false);
            }
            catch (Exception ex) { LoggingService.LogException(ex, "SettingsService.Save"); }
            finally { _ioLock.Release(); }
        });
    }

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                Settings = Sanitize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings());
        }
        catch (Exception ex) { LoggingService.LogException(ex, "SettingsService.Load"); }
    }

    public void ApplyFrom(AppSettings source)
    {
        Settings.Language = source.Language;
        Settings.AccentColor = source.AccentColor;
        Settings.ThemePreset = source.ThemePreset;
        Settings.BackgroundMode = source.BackgroundMode;
        Settings.CardOpacity = source.CardOpacity;
        Settings.FontSize = source.FontSize;
        Settings.SidebarWidth = source.SidebarWidth;
        Settings.CompactMode = source.CompactMode;
        Settings.WingetScope = source.WingetScope;
        Settings.LaunchAtStartup = source.LaunchAtStartup;
        Settings.MinimizeToTray = source.MinimizeToTray;
        Settings.LaunchMode = source.LaunchMode;
        Settings.AiIdleTimeoutMinutes = source.AiIdleTimeoutMinutes;
        Save();
    }

    public void EnableAutostart()
    {
        try
        {
            var key = Registry.CurrentUser
                .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;
            var exePath = Process.GetCurrentProcess().MainModule?.FileName
                ?? AppContext.BaseDirectory + "Coclico.exe";
            if (exePath == null) return;
            key.SetValue("Coclico", '"' + exePath + '"');
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "SettingsService.EnableAutostart");
        }
    }

    public void DisableAutostart()
    {
        try
        {
            var key = Registry.CurrentUser
                .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue("Coclico", throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "SettingsService.DisableAutostart");
        }
    }
}
