using System.Windows;
using System.Windows.Controls;
using Coclico.ViewModels;

namespace Coclico.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
            Loaded   += (_, _) => (DataContext as DashboardViewModel)?.StartRefresh();
            Unloaded += (_, _) => (DataContext as DashboardViewModel)?.StopRefresh();
        }
    }
}
