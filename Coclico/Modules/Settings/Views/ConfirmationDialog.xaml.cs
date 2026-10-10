using System.Windows;

namespace Coclico.Views;

public partial class ConfirmationDialog : Window
{
    public bool DontAskAgain { get; private set; }

    public ConfirmationDialog(string title, string message, string dontAskAgainText)
    {
        InitializeComponent();

        Title = title;
        TxtMessage.Text = message;
        ChkDontAskAgain.Content = dontAskAgainText;
        ChkDontAskAgain.Visibility = string.IsNullOrEmpty(dontAskAgainText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        DontAskAgain = ChkDontAskAgain.IsChecked ?? false;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
