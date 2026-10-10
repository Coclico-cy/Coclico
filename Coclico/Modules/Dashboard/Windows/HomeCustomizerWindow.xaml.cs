using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coclico.Models;
using Coclico.Services;

namespace Coclico.Views;

public class SectionItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
}

public partial class HomeCustomizerWindow : Window
{
    private readonly HomeCustomizationService _service;
    private readonly ObservableCollection<SectionItemViewModel> _sections = [];
    private bool _isInitializing = true;

    public HomeCustomizerWindow()
    {
        InitializeComponent();
        _service = ServiceContainer.GetOptional<HomeCustomizationService>() ?? new HomeCustomizationService();
        SectionsItemsControl.ItemsSource = _sections;

        Opacity = 0;
        Loaded += (_, _) =>
        {
            PlayEntranceAnimation();
            InitializeForm();
        };
    }

    private void PlayEntranceAnimation()
    {
        try
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(180);

            var animScaleX = new DoubleAnimation(0.97, 1.0, dur) { EasingFunction = ease };
            var animScaleY = new DoubleAnimation(0.97, 1.0, dur) { EasingFunction = ease };
            var animFade = new DoubleAnimation(0.0, 1.0, dur) { EasingFunction = ease };

            WinScale.BeginAnimation(ScaleTransform.ScaleXProperty, animScaleX);
            WinScale.BeginAnimation(ScaleTransform.ScaleYProperty, animScaleY);
            BeginAnimation(OpacityProperty, animFade);
        }
        catch
        {
            Opacity = 1;
        }
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (Exception ex) { LoggingService.LogException(ex, "HomeCustomizerWindow.Window_MouseDown"); }
        }
    }

    private void TabBtn_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelShapes == null || PanelStyle == null || PanelOrder == null || PanelTiles == null)
        {
            return;
        }

        PanelShapes.Visibility = Visibility.Collapsed;
        PanelStyle.Visibility = Visibility.Collapsed;
        PanelOrder.Visibility = Visibility.Collapsed;
        PanelTiles.Visibility = Visibility.Collapsed;

        FrameworkElement? target = null;
        if (sender is RadioButton rb)
        {
            target = rb.Tag?.ToString() switch
            {
                "0" => PanelShapes,
                "1" => PanelStyle,
                "2" => PanelOrder,
                "3" => PanelTiles,
                _ => PanelShapes
            };
        }

        if (target != null)
        {
            target.Visibility = Visibility.Visible;
            AnimatePanelTransition(target);
        }
    }

    private static void AnimatePanelTransition(FrameworkElement panel)
    {
        try
        {
            panel.RenderTransform = new TranslateTransform(0, 10);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(140);

            var fade = new DoubleAnimation(0.0, 1.0, dur) { EasingFunction = ease };
            var slide = new DoubleAnimation(10.0, 0.0, dur) { EasingFunction = ease };

            panel.BeginAnimation(OpacityProperty, fade);
            panel.RenderTransform.BeginAnimation(TranslateTransform.YProperty, slide);
        }
        catch (Exception ex) { LoggingService.LogException(ex, "HomeCustomizerWindow.AnimatePanelTransition"); }
    }

    private void InitializeForm()
    {
        _isInitializing = true;
        try
        {
            HomeCustomizationSettings s = _service.Settings;

            RadioShapeRounded.IsChecked = s.Shape == TileShape.Rounded;
            RadioShapePill.IsChecked = s.Shape == TileShape.Pill;
            RadioShapeClassic.IsChecked = s.Shape == TileShape.Classic;
            RadioShapeSquare.IsChecked = s.Shape == TileShape.Square;

            RadioDensityCompact.IsChecked = s.Density == TileDensity.Compact;
            RadioDensityNormal.IsChecked = s.Density == TileDensity.Normal;
            RadioDensitySpacious.IsChecked = s.Density == TileDensity.Spacious;

            RadioCols3.IsChecked = s.GridColumns != 2;
            RadioCols2.IsChecked = s.GridColumns == 2;

            RadioStyleClassic.IsChecked = s.Style == HomeThemeStyle.Classic;
            RadioStyleGlassmorphism.IsChecked = s.Style == HomeThemeStyle.Glassmorphism;
            RadioStyleMinimalist.IsChecked = s.Style == HomeThemeStyle.Minimalist;
            RadioStyleNeon.IsChecked = s.Style == HomeThemeStyle.VibrantNeon;

            ChkTileRam.IsChecked = s.ShowTileRam;
            ChkTileCleaning.IsChecked = s.ShowTileCleaning;
            ChkTilePrograms.IsChecked = s.ShowTilePrograms;
            ChkTileInstaller.IsChecked = s.ShowTileInstaller;
            ChkTileHealth.IsChecked = s.ShowTileHealth;
            ChkTileNetwork.IsChecked = s.ShowTileNetwork;

            LoadSections();
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void LoadSections()
    {
        _sections.Clear();
        HomeCustomizationSettings s = _service.Settings;

        static string GetRes(string key, string fallback)
        {
            return (Application.Current?.Resources[key] as string) ?? fallback;
        }

        var map = new Dictionary<string, (string Title, string Desc, bool Visible)>
        {
            ["Header"] = (GetRes("Customize_Section_Header", "En-tête & Message de bienvenue"),
                          GetRes("Customize_Section_Header_Desc", "Salutation personnalisée et accès aux réglages"),
                          s.ShowHeader),
            ["Metrics"] = (GetRes("Customize_Section_Metrics", "Métriques système en temps réel"),
                           GetRes("Customize_Section_Metrics_Desc", "Jauges instantanées CPU, RAM, Disque et Uptime"),
                           s.ShowMetrics),
            ["QuickModes"] = (GetRes("Customize_Section_QuickModes", "Modes d'optimisation rapides 1-clic"),
                              GetRes("Customize_Section_QuickModes_Desc", "Boutons Zen, Gamer et Travail"),
                              s.ShowQuickModes),
            ["CoreTiles"] = (GetRes("Customize_Section_CoreTiles", "Grille des fonctionnalités Coclico"),
                             GetRes("Customize_Section_CoreTiles_Desc", "Nettoyage, programmes, sécurité et réseau"),
                             s.ShowCoreTiles),
            ["CustomShortcuts"] = (GetRes("Customize_Section_Shortcuts", "Raccourcis & Outils personnalisés"),
                                   GetRes("Customize_Section_Shortcuts_Desc", "Applications .exe, outils Windows et actions"),
                                   s.ShowCustomShortcuts),
            ["RecentActivity"] = (GetRes("Customize_Section_Activity", "Historique de l'activité récente"),
                                  GetRes("Customize_Section_Activity_Desc", "Journal des optimisations récentes"),
                                  s.ShowRecentActivity)
        };

        foreach (string secId in s.SectionOrder)
        {
            if (map.TryGetValue(secId, out (string Title, string Desc, bool Visible) info))
            {
                _sections.Add(new SectionItemViewModel
                {
                    Id = secId,
                    Title = info.Title,
                    Description = info.Desc,
                    IsVisible = info.Visible
                });
            }
        }
    }

    private void ShapeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || sender is not RadioButton rb)
        {
            return;
        }

        string? tag = rb.Tag?.ToString();
        _service.Settings.Shape = tag switch
        {
            "Pill" => TileShape.Pill,
            "Classic" => TileShape.Classic,
            "Square" => TileShape.Square,
            _ => TileShape.Rounded
        };
        _service.NotifyChanged();
    }

    private void DensityRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || sender is not RadioButton rb)
        {
            return;
        }

        string? tag = rb.Tag?.ToString();
        _service.Settings.Density = tag switch
        {
            "Compact" => TileDensity.Compact,
            "Spacious" => TileDensity.Spacious,
            _ => TileDensity.Normal
        };
        _service.NotifyChanged();
    }

    private void ColsRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || sender is not RadioButton rb)
        {
            return;
        }

        _service.Settings.GridColumns = rb.Tag?.ToString() == "2" ? 2 : 3;
        _service.NotifyChanged();
    }

    private void StyleRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || sender is not RadioButton rb)
        {
            return;
        }

        string? tag = rb.Tag?.ToString();
        _service.Settings.Style = tag switch
        {
            "Glassmorphism" => HomeThemeStyle.Glassmorphism,
            "Minimalist" => HomeThemeStyle.Minimalist,
            "VibrantNeon" => HomeThemeStyle.VibrantNeon,
            _ => HomeThemeStyle.Classic
        };
        _service.NotifyChanged();
    }

    private void TileCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _service.Settings.ShowTileRam = ChkTileRam.IsChecked == true;
        _service.Settings.ShowTileCleaning = ChkTileCleaning.IsChecked == true;
        _service.Settings.ShowTilePrograms = ChkTilePrograms.IsChecked == true;
        _service.Settings.ShowTileInstaller = ChkTileInstaller.IsChecked == true;
        _service.Settings.ShowTileHealth = ChkTileHealth.IsChecked == true;
        _service.Settings.ShowTileNetwork = ChkTileNetwork.IsChecked == true;
        _service.NotifyChanged();
    }

    private void SectionVisibility_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || sender is not CheckBox cb)
        {
            return;
        }

        string? id = cb.Tag?.ToString();
        bool isChecked = cb.IsChecked == true;

        switch (id)
        {
            case "Header": _service.Settings.ShowHeader = isChecked; break;
            case "Metrics": _service.Settings.ShowMetrics = isChecked; break;
            case "QuickModes": _service.Settings.ShowQuickModes = isChecked; break;
            case "CoreTiles": _service.Settings.ShowCoreTiles = isChecked; break;
            case "CustomShortcuts": _service.Settings.ShowCustomShortcuts = isChecked; break;
            case "RecentActivity": _service.Settings.ShowRecentActivity = isChecked; break;
        }

        _service.NotifyChanged();
    }

    private void BtnMoveSectionUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
        {
            return;
        }

        List<string> list = _service.Settings.SectionOrder;
        int index = list.IndexOf(id);
        if (index > 0)
        {
            (list[index - 1], list[index]) = (list[index], list[index - 1]);
            _service.NotifyChanged();
            LoadSections();
        }
    }

    private void BtnMoveSectionDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
        {
            return;
        }

        List<string> list = _service.Settings.SectionOrder;
        int index = list.IndexOf(id);
        if (index >= 0 && index < list.Count - 1)
        {
            (list[index + 1], list[index]) = (list[index], list[index + 1]);
            _service.NotifyChanged();
            LoadSections();
        }
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        _service.ResetToDefaults();
        InitializeForm();
        string msg = Application.Current?.Resources["Toast_ResetSuccess"] as string
                  ?? "Disposition et styles de l'Accueil rétablis par défaut.";
        ToastService.Show(msg);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ApplyClose_Click(object sender, RoutedEventArgs e)
    {
        _service.Save();
        string msg = Application.Current?.Resources["Toast_ApplySuccess"] as string
                  ?? "Personnalisation de l'Accueil appliquée avec succès !";
        ToastService.Show(msg);
        Close();
    }
}