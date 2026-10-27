using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Coclico.Services;
using Coclico.ViewModels;

namespace Coclico.Views;

public partial class HealthDefenseView : UserControl
{
    public HealthDefenseView()
    {
        InitializeComponent();
        SystemHealthService healthService = ServiceContainer.GetRequired<SystemHealthService>();
        var vm = new HealthDefenseViewModel(healthService);
        DataContext = vm;

        vm.OutputLog.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && ConsoleListBox.Items.Count > 0)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (ConsoleListBox.Items.Count > 0)
                    {
                        ConsoleListBox.ScrollIntoView(ConsoleListBox.Items[^1]);
                    }
                }, DispatcherPriority.Background);
            }
        };

        ShowTab(PanelDefense, TabDefense);
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button tab || tab.Tag is not string tag)
        {
            return;
        }

        switch (tag)
        {
            case "Startup":
                ShowTab(PanelStartup, TabStartup);
                break;
            case "Smart":
                ShowTab(PanelSmart, TabSmart);
                break;
            default:
                ShowTab(PanelDefense, TabDefense);
                break;
        }
    }

    private void ShowTab(Border panel, Button activeTab)
    {
        PanelDefense.Visibility = Visibility.Collapsed;
        PanelStartup.Visibility = Visibility.Collapsed;
        PanelSmart.Visibility = Visibility.Collapsed;
        panel.Visibility = Visibility.Visible;

        foreach (Button tab in new[] { TabDefense, TabStartup, TabSmart })
        {
            bool isActive = ReferenceEquals(tab, activeTab);
            tab.Background = isActive
                ? FindResource("BgElevatedBrush") as Brush ?? Brushes.Transparent
                : Brushes.Transparent;
            tab.BorderBrush = FindResource(isActive ? "PrimaryBrush" : "BorderSubtleBrush") as Brush ?? tab.BorderBrush;
            tab.Foreground = FindResource(isActive ? "TextPrimaryBrush" : "TextMutedBrush") as Brush ?? tab.Foreground;
            tab.FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal;
        }
    }
}
