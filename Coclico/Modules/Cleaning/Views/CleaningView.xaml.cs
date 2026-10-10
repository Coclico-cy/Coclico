using System.Windows;
using System.Windows.Controls;
using Coclico.Services;
using Coclico.ViewModels;

namespace Coclico.Views;

public partial class CleaningView : UserControl
{
    public CleaningViewModel ViewModel { get; }

    public CleaningView()
    {
        InitializeComponent();
        ViewModel = ServiceContainer.GetRequired<CleaningViewModel>();
        DataContext = ViewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.AttachLanguageChanged();
        await ViewModel.ScanAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.DetachLanguageChanged();
    }
}
