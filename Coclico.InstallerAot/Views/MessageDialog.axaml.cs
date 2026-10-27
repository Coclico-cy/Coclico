using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Coclico.Installer.Views;

public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    private void BtnOk_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    public static async Task ShowErrorAsync(Window owner, string title, string message)
    {
        var dialog = new MessageDialog
        {
            DlgTitle = { Text = title },
            DlgMessage = { Text = message }
        };
        await dialog.ShowDialog(owner);
    }

    public static async Task ShowWarningAsync(Window owner, string title, string message)
    {
        var dialog = new MessageDialog
        {
            DlgTitle = { Text = title },
            DlgMessage = { Text = message }
        };
        await dialog.ShowDialog(owner);
    }
}
