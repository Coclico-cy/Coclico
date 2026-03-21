using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Coclico.Services;
using Coclico.ViewModels;

namespace Coclico.Views
{
    public partial class ScannerView : System.Windows.Controls.UserControl
    {
        private ICollectionView? _view;

        public ScannerView()
        {
            InitializeComponent();
            var vm = new ScannerViewModel();
            DataContext = vm;

            _view = CollectionViewSource.GetDefaultView(vm.Programs);
            ResultsList.ItemsSource = _view;
        }

        private void AttachFilter(ScannerViewModel vm)
        {
            _view = CollectionViewSource.GetDefaultView(vm.Programs);
            ResultsList.ItemsSource = _view;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_view == null) return;
            var text = SearchBox.Text.Trim();
            _view.Filter = string.IsNullOrEmpty(text)
                ? null
                : o => o is Coclico.Services.InstalledProgramsService.ProgramInfo p &&
                       (p.Name.Contains(text, System.StringComparison.OrdinalIgnoreCase) ||
                        p.Publisher.Contains(text, System.StringComparison.OrdinalIgnoreCase) ||
                        p.Source.Contains(text, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
