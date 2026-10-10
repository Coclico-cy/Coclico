using System.Windows;
using System.Windows.Controls;
using Coclico.Services;

namespace Coclico.Views;

public partial class HelpView : UserControl
{
    private readonly LocalizationService? _loc;

    public HelpView()
    {
        InitializeComponent();

        _loc = ServiceContainer.GetOptional<LocalizationService>();
        if (_loc != null)
        {
            _loc.LanguageChanged += OnLanguageChanged;
        }

        Unloaded += (_, _) =>
        {
            if (_loc != null)
            {
                _loc.LanguageChanged -= OnLanguageChanged;
            }
        };

        ApplyVersionTexts();
    }

    private void OnLanguageChanged(string? lang)
    {
        _ = Dispatcher.InvokeAsync(ApplyVersionTexts);
    }

    private void ApplyVersionTexts()
    {
        var version = UpdateCheckService.GetCurrentVersion();

        TxtVersionTitle.Text = string.Format(GetTemplate("Help_Version_Title"), version);
        TxtVersionTag.Text = string.Format(GetTemplate("Help_Version_CurrentTag"), version);
        TxtVersionCardTitle.Text = $"Coclico {version} Beta";
        TxtVersionDesc.Text = string.Format(GetTemplate("Help_Version_Desc"), version);
        TxtVersionNotesTitle.Text = string.Format(GetTemplate("Help_Version_NotesTitle"), version);
        TxtInfoStatusText.Text = string.Format(GetTemplate("Help_Info_Status_Text"), version);
    }

    private string GetTemplate(string key)
    {
        return _loc?.Get(key) ?? (TryFindResource(key) as string ?? key);
    }

    private void Section_Click(object sender, RoutedEventArgs e)
    {
        if (PanelGuide == null || PanelVersion == null || PanelInfo == null)
        {
            return;
        }

        PanelGuide.Visibility = Visibility.Collapsed;
        PanelVersion.Visibility = Visibility.Collapsed;
        PanelInfo.Visibility = Visibility.Collapsed;

        string? tag = (sender as ListBoxItem)?.Tag?.ToString();
        switch (tag)
        {
            case "Guide":
                PanelGuide.Visibility = Visibility.Visible;
                break;
            case "Version":
                PanelVersion.Visibility = Visibility.Visible;
                break;
            case "Info":
                PanelInfo.Visibility = Visibility.Visible;
                break;
        }
    }
}
