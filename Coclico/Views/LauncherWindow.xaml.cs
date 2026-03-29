#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Coclico.Views;

public partial class LauncherWindow : Window
{
    public LaunchMode SelectedMode { get; private set; } = LaunchMode.Normal;
    public bool LaunchAtStartup { get; private set; } = false;
    public bool MinimizeToTray { get; private set; } = false;

    private static readonly SolidColorBrush _accentBorder =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED"));
    private static readonly SolidColorBrush _mutedBorder =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A1848"));
    private static readonly SolidColorBrush _accentBg =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#18093A"));
    private static readonly SolidColorBrush _mutedBg =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0D0818"));
    private static readonly SolidColorBrush _accentText =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EDE9F7"));
    private static readonly SolidColorBrush _mutedText =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5A4080"));

    public LauncherWindow(bool currentLaunchAtStartup = false, bool currentMinimizeToTray = false)
    {
        InitializeComponent();

        LaunchAtStartup = currentLaunchAtStartup;
        MinimizeToTray = currentMinimizeToTray;

        ToggleLaunchAtStartup.IsChecked = LaunchAtStartup;
        ToggleMinimizeToTray.IsChecked = MinimizeToTray;

        SelectCard(LaunchMode.Normal);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CardNormal_Click(object sender, MouseButtonEventArgs e) => SelectCard(LaunchMode.Normal);
    private void CardMinimized_Click(object sender, MouseButtonEventArgs e) => SelectCard(LaunchMode.Minimized);
    private void CardTray_Click(object sender, MouseButtonEventArgs e) => SelectCard(LaunchMode.Tray);

    private void SelectCard(LaunchMode mode)
    {
        SelectedMode = mode;

        RadioNormal.IsChecked = mode == LaunchMode.Normal;
        RadioMinimized.IsChecked = mode == LaunchMode.Minimized;
        RadioTray.IsChecked = mode == LaunchMode.Tray;

        ApplyCardState(CardNormal, GlowNormal, TitleNormal, IconNormal, mode == LaunchMode.Normal);
        ApplyCardState(CardMinimized, GlowMinimized, TitleMinimized, IconMinimized, mode == LaunchMode.Minimized);
        ApplyCardState(CardTray, GlowTray, TitleTray, IconTray, mode == LaunchMode.Tray);
    }

    private static void ApplyCardState(
        Border card,
        DropShadowEffect glow,
        TextBlock title,
        Wpf.Ui.Controls.SymbolIcon icon,
        bool selected)
    {
        if (selected)
        {
            card.BorderBrush = _accentBorder;
            card.Background = _accentBg;
            glow.BlurRadius = 14;
            glow.Opacity = 0.55;
            title.Foreground = _accentText;
            icon.Foreground = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#A78BFA"),
                (Color)ColorConverter.ConvertFromString("#7C3AED"),
                new Point(0, 0), new Point(1, 1));
        }
        else
        {
            card.BorderBrush = _mutedBorder;
            card.Background = _mutedBg;
            glow.BlurRadius = 0;
            glow.Opacity = 0;
            title.Foreground = _mutedText;
            icon.Foreground = _mutedText;
        }
    }

    private void ToggleLaunchAtStartup_Click(object sender, RoutedEventArgs e)
    {
        LaunchAtStartup = ToggleLaunchAtStartup.IsChecked == true;
    }

    private void ToggleMinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        MinimizeToTray = ToggleMinimizeToTray.IsChecked == true;
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        LaunchAtStartup = ToggleLaunchAtStartup.IsChecked == true;
        MinimizeToTray = ToggleMinimizeToTray.IsChecked == true;

        DialogResult = true;
        Close();
    }
}
