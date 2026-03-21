using System.Windows;
using System.Windows.Controls;
using Coclico.ViewModels;

namespace Coclico.Views
{
    public partial class CleaningView : System.Windows.Controls.UserControl
    {
        public CleaningViewModel ViewModel { get; } = new();

        public CleaningView()
        {
            InitializeComponent();
            DataContext = ViewModel;
        }

        private async void BtnStartCleaning_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.StartCleaningAsync();
        }

        private async void BtnStartDeepCleaning_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.StartDeepCleaningAsync();
        }

        private void BtnCancelCleaning_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.CancelCleaning();
        }
    }
}
