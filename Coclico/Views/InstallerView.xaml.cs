#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Coclico.Services;

namespace Coclico.Views;

public partial class InstallerView : UserControl, INotifyPropertyChanged
{
    private readonly InstallerService _installerService = new();
    private readonly Dictionary<string, CancellationTokenSource> _installationTokens = new();
    private List<InstallerService.SoftwareItem> _allSoftwareItems = new();

    public ObservableCollection<CategoryGroup> CategorizedSoftware { get; } = new();

    public ICommand InstallCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand UpgradeAllCommand { get; }

    public bool IsAdminWarningVisible => !_installerService.IsRunAsAdmin();

    private bool _isTestRunning;
    public bool IsTestRunning
    {
        get => _isTestRunning;
        set { _isTestRunning = value; OnNotifyPropertyChanged(); }
    }

    private string _testProgressText = "";
    public string TestProgressText
    {
        get => _testProgressText;
        set { _testProgressText = value; OnNotifyPropertyChanged(); }
    }

    private string _testTimer = "00:00";
    public string TestTimer
    {
        get => _testTimer;
        set { _testTimer = value; OnNotifyPropertyChanged(); }
    }

    private string _testCurrentLog = "";
    public string TestCurrentLog
    {
        get => _testCurrentLog;
        set { _testCurrentLog = value; OnNotifyPropertyChanged(); }
    }

    private double _testProgressValue;
    public double TestProgressValue
    {
        get => _testProgressValue;
        set { _testProgressValue = value; OnNotifyPropertyChanged(); }
    }

    public InstallerView()
    {
        InitializeComponent();
        DataContext = this;

        InstallCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand<object?>(async p => await InstallAsync(p as InstallerService.SoftwareItem));
        CancelCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(p => CancelInstallation(p as InstallerService.SoftwareItem));
        UpgradeAllCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand<object?>(async _ => await UpgradeAllAsync());

        LoadSoftware();
    }

    private async Task UpgradeAllAsync()
    {
        if (MessageBox.Show("Voulez-vous mettre \u00e0 jour TOUS les logiciels install\u00e9s sur ce PC via Winget ?", "Mise \u00e0 jour globale", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            IsTestRunning = true;
            TestProgressText = "Mise \u00e0 jour globale en cours...";
            TestTimer = "En cours";
            TestProgressValue = 0;

            await _installerService.UpgradeAllAsync(output =>
            {
                Dispatcher.InvokeAsync(() => TestCurrentLog = output);
            });

            IsTestRunning = false;
            MessageBox.Show("Mise \u00e0 jour globale termin\u00e9e.", "Succ\u00e8s", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CancelInstallation(InstallerService.SoftwareItem? software)
    {
        if (software != null && _installationTokens.TryGetValue(software.WingetId, out var cts))
        {
            cts.Cancel();
            software.Status = "Annulation...";
        }
    }

    private void LoadSoftware()
    {
        _allSoftwareItems = _installerService.GetAvailableSoftware().ToList();
        RebuildGroups(_allSoftwareItems);
    }

    private void RebuildGroups(IEnumerable<InstallerService.SoftwareItem> source)
    {
        CategorizedSoftware.Clear();
        var groups = source.GroupBy(s => s.Category)
                           .Select(g => new CategoryGroup
                           {
                               CategoryName = g.Key,
                               Items = new ObservableCollection<InstallerService.SoftwareItem>(g)
                           });
        foreach (var group in groups)
            CategorizedSoftware.Add(group);
    }

    private void InstallerSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        var filter = tb.Text.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _allSoftwareItems
            : _allSoftwareItems.Where(s => s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        RebuildGroups(filtered);
    }

    private async Task InstallAsync(InstallerService.SoftwareItem? software)
    {
        if (software == null || software.IsInstalling) return;

        var cts = new CancellationTokenSource();
        _installationTokens[software.WingetId] = cts;

        software.IsInstalling = true;
        software.Status = "Initialisation...";
        software.LastOutput = "V\u00e9rification des d\u00e9pendances...";
        software.ProgressValue = 0;

        bool success = await _installerService.InstallSoftwareAsync(software, output =>
        {
            Dispatcher.Invoke(() => software.AppendOutput(output));
        }, cts.Token);

        _installationTokens.Remove(software.WingetId);
        software.IsInstalling = false;

        if (success)
        {
            software.Status = "Install\u00e9";
        }
        else if (cts.IsCancellationRequested)
        {
            software.Status = "Annul\u00e9";
        }
        else
        {
            software.Status = "\u00c9chec";
            if (string.IsNullOrEmpty(software.LastOutput) || software.LastOutput == "Pr\u00eat")
            {
                software.LastOutput = "Le processus s'est arr\u00eat\u00e9 de mani\u00e8re inattendue.";
            }
        }
    }

    private void BtnRelaunchAdmin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? "Coclico.exe";

            Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas"
            });

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "InstallerView.RelaunchAdmin");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnNotifyPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public class CategoryGroup
{
    public string CategoryName { get; set; } = string.Empty;
    public ObservableCollection<InstallerService.SoftwareItem> Items { get; set; } = new();
}
