using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Coclico.Models;

namespace Coclico.Services;

public sealed class HomeCustomizationService
{
    private static readonly string CustomizationFilePath = Path.Combine(
        SettingsService.GetUserDataDirectory(), "home_customization.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly SolidColorBrush MinimalistBg = Frozen(Color.FromRgb(14, 17, 24));
    private static readonly SolidColorBrush MinimalistBorder = Frozen(Color.FromRgb(30, 38, 54));
    private static readonly SolidColorBrush GlassBg = Frozen(Color.FromArgb(170, 18, 24, 42));
    private static readonly SolidColorBrush GlassBorder = Frozen(Color.FromArgb(120, 99, 102, 241));

    public HomeCustomizationSettings Settings { get; private set; } = new();

    public event EventHandler? CustomizationChanged;

    public HomeCustomizationService()
    {
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(CustomizationFilePath))
            {
                string json = File.ReadAllText(CustomizationFilePath);
                HomeCustomizationSettings? loaded = JsonSerializer.Deserialize<HomeCustomizationSettings>(json);
                if (loaded != null)
                {
                    Settings = loaded;
                    EnsureDefaultSections(Settings);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "HomeCustomizationService.Load");
        }

        Settings = new HomeCustomizationSettings();
        Save();
    }

    private static void EnsureDefaultSections(HomeCustomizationSettings settings)
    {
        settings.SectionOrder ??= [];
        foreach (string sec in HomeCustomizationSettings.DefaultSectionOrder)
        {
            if (!settings.SectionOrder.Contains(sec))
            {
                settings.SectionOrder.Add(sec);
            }
        }
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(Settings, JsonOptions);
            File.WriteAllText(CustomizationFilePath, json);
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                CustomizationChanged?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "HomeCustomizationService.Save");
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            string json = JsonSerializer.Serialize(Settings, JsonOptions);
            await File.WriteAllTextAsync(CustomizationFilePath, json).ConfigureAwait(false);
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                CustomizationChanged?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "HomeCustomizationService.SaveAsync");
        }
    }

    public void ResetToDefaults()
    {
        Settings = new HomeCustomizationSettings();
        Save();
    }

    public void NotifyChanged()
    {
        Save();
    }

    public CornerRadius TileCornerRadius => Settings.Shape switch
    {
        TileShape.Square => new CornerRadius(4),
        TileShape.Classic => new CornerRadius(10),
        TileShape.Rounded => new CornerRadius(16),
        TileShape.Pill => new CornerRadius(24),
        _ => new CornerRadius(14)
    };

    public CornerRadius CardCornerRadius => Settings.Shape switch
    {
        TileShape.Square => new CornerRadius(6),
        TileShape.Classic => new CornerRadius(12),
        TileShape.Rounded => new CornerRadius(18),
        TileShape.Pill => new CornerRadius(26),
        _ => new CornerRadius(16)
    };

    public CornerRadius IconCornerRadius => Settings.Shape switch
    {
        TileShape.Square => new CornerRadius(4),
        TileShape.Classic => new CornerRadius(8),
        TileShape.Rounded => new CornerRadius(12),
        TileShape.Pill => new CornerRadius(20),
        _ => new CornerRadius(10)
    };

    public int GridColumns => Math.Clamp(Settings.GridColumns, 2, 5);

    public Thickness TilePadding => Settings.Density switch
    {
        TileDensity.Compact => new Thickness(14, 10, 14, 10),
        TileDensity.Spacious => new Thickness(24, 22, 24, 22),
        _ => new Thickness(20, 16, 20, 16)
    };

    public double TileIconBoxSize => Settings.Density switch
    {
        TileDensity.Compact => 38,
        TileDensity.Spacious => 54,
        _ => 46
    };

    public double TileIconSymbolSize => Settings.Density switch
    {
        TileDensity.Compact => 20,
        TileDensity.Spacious => 28,
        _ => 24
    };

    public double TileTitleFontSize => Settings.Density switch
    {
        TileDensity.Compact => 13.5,
        TileDensity.Spacious => 16.5,
        _ => 15
    };

    public double TileDescFontSize => Settings.Density switch
    {
        TileDensity.Compact => 10,
        TileDensity.Spacious => 12,
        _ => 11
    };

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public Brush GetTileBg(string tileKey, Brush defaultBg)
    {
        return Settings.Style switch
        {
            HomeThemeStyle.Minimalist => MinimalistBg,
            HomeThemeStyle.Glassmorphism => GlassBg,
            HomeThemeStyle.VibrantNeon => defaultBg,
            _ => defaultBg
        };
    }

    public Brush GetTileBorder(string tileKey, Brush defaultBorder, Brush neonAccent)
    {
        return Settings.Style switch
        {
            HomeThemeStyle.Minimalist => MinimalistBorder,
            HomeThemeStyle.Glassmorphism => GlassBorder,
            HomeThemeStyle.VibrantNeon => neonAccent,
            _ => defaultBorder
        };
    }
}