using System.Windows;
using System.Windows.Controls;
using Coclico.ViewModels;

namespace Coclico.Views
{
    public partial class RuleSelectionDialog : System.Windows.Controls.UserControl
    {
        public FlowViewModel.RuleType? SelectedRule { get; private set; }

        public RuleSelectionDialog()
        {
            InitializeComponent();
            RuleComboBox.ItemsSource = System.Enum.GetValues<FlowViewModel.RuleType>();
            RuleComboBox.SelectedIndex = 0;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (RuleComboBox.SelectedItem is FlowViewModel.RuleType rule)
            {
                SelectedRule = rule;
            }
            if (Window.GetWindow(this) is Window w)
            {
                w.DialogResult = true;
                w.Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedRule = null;
            if (Window.GetWindow(this) is Window w)
            {
                w.DialogResult = false;
                w.Close();
            }
        }
    }
}
