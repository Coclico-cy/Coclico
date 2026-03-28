#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Coclico.Services;
using Coclico.ViewModels;

namespace Coclico.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        Loaded   += (_, _) => (DataContext as DashboardViewModel)?.StartRefresh();
        Unloaded += (_, _) => (DataContext as DashboardViewModel)?.StopRefresh();
    }

    private async void ModeZen_Click(object sender, MouseButtonEventArgs e)
    {
        try { await RunModeAsync(MemoryCleanerService.CleanProfile.Quick, cleanTemp: true); }
        catch (Exception ex) { LoggingService.LogException(ex, "DashboardView.ModeZen_Click"); }
    }

    private async void ModeGamer_Click(object sender, MouseButtonEventArgs e)
    {
        try { await RunModeAsync(MemoryCleanerService.CleanProfile.Deep, cleanTemp: false); }
        catch (Exception ex) { LoggingService.LogException(ex, "DashboardView.ModeGamer_Click"); }
    }

    private async void ModeWork_Click(object sender, MouseButtonEventArgs e)
    {
        try { await RunModeAsync(MemoryCleanerService.CleanProfile.Normal, cleanTemp: false); }
        catch (Exception ex) { LoggingService.LogException(ex, "DashboardView.ModeWork_Click"); }
    }

    private async Task RunModeAsync(MemoryCleanerService.CleanProfile profile, bool cleanTemp)
    {
        try
        {
            await MemoryCleanerService.CleanByProfileAsync(profile);

            if (cleanTemp)
            {
                string temp = Path.GetTempPath();
                await Task.Run(() =>
                {
                    foreach (var f in Directory.EnumerateFiles(temp, "*",
                        SearchOption.TopDirectoryOnly))
                    {
                        try { File.Delete(f); } catch { }
                    }
                });
            }

            var key = Application.Current.Resources["Dashboard_ModeActivated"] as string ?? "Mode activ\u00e9 !";
            ToastService.Show("\u2713 " + key);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "DashboardView.RunModeAsync");
        }
    }
}
