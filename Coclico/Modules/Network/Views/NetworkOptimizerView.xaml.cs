using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Coclico.Models.Network;
using Coclico.Services;
using Coclico.ViewModels;

namespace Coclico.Views;

public partial class NetworkOptimizerView : UserControl
{
    public NetworkOptimizerViewModel ViewModel { get; }

    public NetworkOptimizerView()
    {
        InitializeComponent();
        ViewModel = ServiceContainer.GetOptional<NetworkOptimizerViewModel>() ?? new NetworkOptimizerViewModel();
        DataContext = ViewModel;

        ((INotifyCollectionChanged)ViewModel.OutputLog).CollectionChanged += (_, _) =>
        {
            if (ConsoleScroll.Dispatcher.CheckAccess())
            {
                ConsoleScroll.ScrollToEnd();
            }
            else
            {
                _ = ConsoleScroll.Dispatcher.InvokeAsync(() => ConsoleScroll.ScrollToEnd());
            }
        };
    }

    private void AdapterRadio_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NetworkAdapterInfo adapter })
        {
            ViewModel.SelectedAdapter = adapter;
        }
    }

    private void PresetFast_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedPreset = NetworkOptimizationPreset.Fast;
    }

    private void PresetGaming_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedPreset = NetworkOptimizationPreset.GamingUltra;
    }

    private void PresetThroughput_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedPreset = NetworkOptimizationPreset.MaxThroughput;
    }

    private void ProfileGaming_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedCognitiveProfile = OptimizationProfile.Gaming;
    }

    private void ProfileLowLatency_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedCognitiveProfile = OptimizationProfile.LowLatency;
    }

    private void ProfileThroughput_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedCognitiveProfile = OptimizationProfile.MaxThroughput;
    }

    private void ProfileBalanced_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedCognitiveProfile = OptimizationProfile.Balanced;
    }

    private void ModeAuto_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedExecutionMode = EngineExecutionMode.Auto;
    }

    private void ModeSafe_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedExecutionMode = EngineExecutionMode.Safe;
    }

    private void ModeSimulation_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedExecutionMode = EngineExecutionMode.Simulation;
    }

    private void ModeExpert_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedExecutionMode = EngineExecutionMode.Expert;
    }

    private void ScopeAll_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedScopeFilter = "Tous";
    }

    private void ScopeNdis_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedScopeFilter = "NDIS Pilote";
    }

    private void ScopeTcp_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedScopeFilter = "TCP Kernel";
    }

    private void ScopeQos_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedScopeFilter = "Windows QoS";
    }

    private void ScopeMtu_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedScopeFilter = "MTU & Trame";
    }

    private void ScopeIpv6_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedScopeFilter = "IPv6 & Tunneling";
    }

    private void TabLaboratoire_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedTabIndex = 0;
    }

    private void TabMatrix_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedTabIndex = 1;
    }

    private void TabResults_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedTabIndex = 2;
    }

    private void TabTools_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedTabIndex = 0;
    }

    private void TabLogs_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedTabIndex = 3;
    }
}