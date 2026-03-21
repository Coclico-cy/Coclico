using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Coclico.Services;

namespace Coclico.Views
{
    public partial class InstallerView : System.Windows.Controls.UserControl, INotifyPropertyChanged
    {
        private readonly InstallerService _installerService = new();
        private readonly Dictionary<string, System.Threading.CancellationTokenSource> _installationTokens = new();
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

            InstallCommand = new RelayCommand(async p => await InstallAsync(p as InstallerService.SoftwareItem));
            CancelCommand = new RelayCommand(p => CancelInstallation(p as InstallerService.SoftwareItem));
            UpgradeAllCommand = new RelayCommand(async _ => await UpgradeAllAsync());

            LoadSoftware();
        }

        private async Task UpgradeAllAsync()
        {
            if (System.Windows.MessageBox.Show("Voulez-vous mettre à jour TOUS les logiciels installés sur ce PC via Winget ?", "Mise à jour globale", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
            {
                IsTestRunning = true;
                TestProgressText = "Mise à jour globale en cours...";
                TestTimer = "En cours";
                TestProgressValue = 0;

                await _installerService.UpgradeAllAsync(output =>
                {
                    Dispatcher.InvokeAsync(() => TestCurrentLog = output);
                });

                IsTestRunning = false;
                System.Windows.MessageBox.Show("Mise à jour globale terminée.", "Succès", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
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

            var cts = new System.Threading.CancellationTokenSource();
            _installationTokens[software.WingetId] = cts;

            software.IsInstalling = true;
            software.Status = "Initialisation...";
            software.LastOutput = "Vérification des dépendances...";
            software.ProgressValue = 0;

                bool success = await _installerService.InstallSoftwareAsync(software, output =>
                {
                    Dispatcher.Invoke(() => software.AppendOutput(output));
                }, cts.Token);

            _installationTokens.Remove(software.WingetId);
            software.IsInstalling = false;
            
            if (success)
            {
                software.Status = "Installé";
            }
            else if (cts.IsCancellationRequested)
            {
                software.Status = "Annulé";
            }
            else
            {
                software.Status = "Échec";
                if (string.IsNullOrEmpty(software.LastOutput) || software.LastOutput == "Prêt")
                {
                    software.LastOutput = "Le processus s'est arrêté de manière inattendue.";
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
}
