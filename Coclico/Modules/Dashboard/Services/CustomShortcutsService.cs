using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coclico.Services;

public enum ShortcutType
{
    CoclicoAction,
    WindowsTool,
    CustomApp
}

public sealed class CustomShortcut
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("subtitle")]
    public string Subtitle { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public ShortcutType Type { get; set; } = ShortcutType.WindowsTool;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("iconSymbol")]
    public string IconSymbol { get; set; } = "Apps24";

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#6366F1";

    [JsonPropertyName("bgTint")]
    public string BgTint { get; set; } = "#13182E";
}

public sealed class CustomShortcutsService
{
    private static readonly string ShortcutsFilePath = Path.Combine(
        SettingsService.GetUserDataDirectory(), "shortcuts.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ObservableCollection<CustomShortcut> Shortcuts { get; } = [];

    public CustomShortcutsService()
    {
        Load();
    }

    public void Load()
    {
        try
        {
            Shortcuts.Clear();
            if (File.Exists(ShortcutsFilePath))
            {
                string json = File.ReadAllText(ShortcutsFilePath);
                List<CustomShortcut>? items = JsonSerializer.Deserialize<List<CustomShortcut>>(json);
                if (items != null && items.Count > 0)
                {
                    foreach (CustomShortcut item in items)
                    {
                        Shortcuts.Add(item);
                    }

                    return;
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CustomShortcutsService.Load");
        }

        List<CustomShortcut> defaults = GetDefaultShortcuts();
        foreach (CustomShortcut item in defaults)
        {
            Shortcuts.Add(item);
        }

        Save();
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(Shortcuts.ToList(), JsonOptions);
            File.WriteAllText(ShortcutsFilePath, json);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CustomShortcutsService.Save");
        }
    }

    public void AddShortcut(CustomShortcut shortcut)
    {
        Shortcuts.Add(shortcut);
        Save();
    }

    public void RemoveShortcut(string id)
    {
        CustomShortcut? existing = Shortcuts.FirstOrDefault(s => s.Id == id);
        if (existing != null)
        {
            _ = Shortcuts.Remove(existing);
            Save();
        }
    }

    public static List<CustomShortcut> GetDefaultShortcuts()
    {
        return
        [
            new CustomShortcut
            {
                Title = "Vider la RAM",
                Subtitle = "Purge immédiate de la mémoire",
                Type = ShortcutType.CoclicoAction,
                Target = "ACTION_CLEAN_RAM",
                IconSymbol = "Memory20",
                AccentColor = "#818CF8",
                BgTint = "#13182E"
            },
            new CustomShortcut
            {
                Title = "Vider les Temp",
                Subtitle = "Nettoyage fichiers inutiles",
                Type = ShortcutType.CoclicoAction,
                Target = "NAV_CLEANING",
                IconSymbol = "Delete24",
                AccentColor = "#10D9A0",
                BgTint = "#081F17"
            },
            new CustomShortcut
            {
                Title = "Gestionnaire des tâches",
                Subtitle = "Processus et performances",
                Type = ShortcutType.WindowsTool,
                Target = "taskmgr.exe",
                IconSymbol = "DesktopPulse24",
                AccentColor = "#06B6D4",
                BgTint = "#081E29"
            },
            new CustomShortcut
            {
                Title = "PowerShell",
                Subtitle = "Console système administrateur",
                Type = ShortcutType.WindowsTool,
                Target = "powershell.exe",
                IconSymbol = "WindowConsole20",
                AccentColor = "#38BDF8",
                BgTint = "#081C2E"
            },
            new CustomShortcut
            {
                Title = "Nettoyage Disque",
                Subtitle = "Utilitaire Cleanmgr Windows",
                Type = ShortcutType.WindowsTool,
                Target = "cleanmgr.exe",
                IconSymbol = "HardDrive24",
                AccentColor = "#F59E0B",
                BgTint = "#241804"
            },
            new CustomShortcut
            {
                Title = "Paramètres Windows",
                Subtitle = "Configuration système Microsoft",
                Type = ShortcutType.WindowsTool,
                Target = "ms-settings:",
                IconSymbol = "Settings24",
                AccentColor = "#EC4899",
                BgTint = "#260A18"
            }
        ];
    }

    public async Task ExecuteShortcutAsync(CustomShortcut shortcut, Action<string>? navigateCallback)
    {
        try
        {
            switch (shortcut.Type)
            {
                case ShortcutType.CoclicoAction:
                    if (shortcut.Target == "ACTION_CLEAN_RAM")
                    {
                        ToastService.ShowInfo("Optimisation de la mémoire vive en cours...");
                        MemoryCleanerService.CleanResult result = await MemoryCleanerService.FullCleanAsync(false).ConfigureAwait(false);
                        long freedMb = result.TotalFreed / (1024 * 1024);
                        ToastService.Show($"✅ Mémoire libérée avec succès : ~{freedMb} Mo !");
                    }
                    else if (shortcut.Target.StartsWith("NAV_"))
                    {
                        string tag = shortcut.Target.Replace("NAV_", "");
                        navigateCallback?.Invoke(tag);
                    }
                    break;

                case ShortcutType.WindowsTool:
                case ShortcutType.CustomApp:
                    if (!string.IsNullOrWhiteSpace(shortcut.Target))
                    {
                        if (!IsTargetAllowedByPolicy(shortcut.Target, shortcut.Type))
                        {
                            LoggingService.LogWarning(
                                $"[CustomShortcuts] Lancement bloqué par la politique de sécurité : '{shortcut.Title}' -> {shortcut.Target}");
                            ToastService.Show($"'{shortcut.Title}' a été bloqué par la politique de sécurité.");
                            break;
                        }

                        _ = Process.Start(new ProcessStartInfo(shortcut.Target) { UseShellExecute = true });
                        ToastService.Show($"Lancement de '{shortcut.Title}'");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, $"CustomShortcutsService.ExecuteShortcutAsync({shortcut.Title})");
            ToastService.Show($"Impossible de lancer '{shortcut.Title}' : {ex.Message}");
        }
    }

    private static bool IsTargetAllowedByPolicy(string target, ShortcutType type)
    {
        string trimmed = target.Trim().Trim('"', '\'');
        if (trimmed.Length == 0)
        {
            return false;
        }

        ISecurityPolicy? policy = ServiceContainer.GetOptional<ISecurityPolicy>();

        if (policy?.IsCommandBlocked(trimmed.ToLowerInvariant()) == true)
        {
            return false;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }

        if (type == ShortcutType.WindowsTool)
        {
            return policy == null || policy.IsApplicationAllowed(trimmed, out _);
        }

        if (policy != null && policy.IsProtectedPath(trimmed.ToLowerInvariant()))
        {
            return false;
        }

        return true;
    }
}