#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using Coclico.Services;
using Coclico.ViewModels;

namespace Coclico.Views;

public partial class CleaningView : UserControl
{
    public CleaningViewModel ViewModel { get; } = new();

    public CleaningView()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    private async void BtnStartCleaning_Click(object sender, RoutedEventArgs e)
    {
        try { await ViewModel.StartCleaningAsync(); }
        catch (Exception ex) { LoggingService.LogException(ex, "CleaningView.BtnStartCleaning_Click"); }
    }

    private async void BtnStartDeepCleaning_Click(object sender, RoutedEventArgs e)
    {
        try { await ViewModel.StartDeepCleaningAsync(); }
        catch (Exception ex) { LoggingService.LogException(ex, "CleaningView.BtnStartDeepCleaning_Click"); }
    }

    private void BtnCancelCleaning_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelCleaning();
    }
}
