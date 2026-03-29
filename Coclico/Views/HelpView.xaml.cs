#nullable enable
using System.Windows;
using System.Windows.Controls;

namespace Coclico.Views;

public partial class HelpView : UserControl
{
    public HelpView()
    {
        InitializeComponent();
    }

    private void Section_Click(object sender, RoutedEventArgs e)
    {
        if (PanelGuide == null) return;

        PanelGuide.Visibility = Visibility.Collapsed;
        PanelChangelog.Visibility = Visibility.Collapsed;

        string? tag = (sender as ListBoxItem)?.Tag?.ToString();
        switch (tag)
        {
            case "Guide": PanelGuide.Visibility = Visibility.Visible; break;
            case "Changelog": PanelChangelog.Visibility = Visibility.Visible; break;
        }
    }
}
