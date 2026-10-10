using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Coclico.Services;
using CommunityToolkit.Mvvm.Input;

namespace Coclico.ViewModels;

public class InstallerViewModel : INotifyPropertyChanged
{
    private readonly InstallerService _installerService;
    private readonly LocalizationService? _loc;
    private CancellationTokenSource? _cts;

    public ObservableCollection<InstallerService.WingetPackage> EssentialPackages { get; } = [];
    public ObservableCollection<InstallerService.WingetPackage> FilteredEssentials { get; } = [];
    public ObservableCollection<InstallerService.WingetPackage> AvailableUpgrades { get; } = [];
    public ObservableCollection<InstallerService.WingetPackage> SearchResults { get; } = [];
    public ObservableCollection<string> Categories { get; } = [];
    public ObservableCollection<string> OutputLog { get; } = [];

    public string CurrentTab
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTabEssentials));
                OnPropertyChanged(nameof(IsTabUpgrades));
                OnPropertyChanged(nameof(IsTabSearch));
            }
        }
    } = "Essentials";

    public bool IsTabEssentials => CurrentTab == "Essentials";
    public bool IsTabUpgrades => CurrentTab == "Upgrades";
    public bool IsTabSearch => CurrentTab == "Search";

    public string SearchQuery
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = string.Empty;

    public string SelectedCategory
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                FilterEssentials();
            }
        }
    } = "Toutes";

    public bool IsOperating
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotBusy));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsSearching
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotBusy));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsCheckingUpgrades
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotBusy));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsNotBusy => !IsOperating && !IsSearching && !IsCheckingUpgrades;

    public string OperationStatus
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = "Prêt";

    public int UpgradesCount => AvailableUpgrades.Count;
    public bool HasUpgrades => AvailableUpgrades.Count > 0;
    public string UpgradesCountText => string.Format(_loc?.Get("Installer_AvailableCount") ?? "{0} disponible(s)", UpgradesCount);
    public bool HasSearchResults => SearchResults.Count > 0;
    public int SelectedEssentialsCount => EssentialPackages.Count(p => p.IsSelected);
    public string SelectedEssentialsText => string.Format(_loc?.Get("Installer_SelectedCount") ?? "{0} sélectionné(s)", SelectedEssentialsCount);
    public bool HasSelectedEssentials => SelectedEssentialsCount > 0;

    public IRelayCommand<string> SelectTabCommand { get; }
    public IRelayCommand<string> SelectCategoryCommand { get; }
    public IRelayCommand SelectAllEssentialsCommand { get; }
    public IRelayCommand DeselectAllEssentialsCommand { get; }
    public IAsyncRelayCommand CheckUpgradesCommand { get; }
    public IAsyncRelayCommand UpgradeAllCommand { get; }
    public IAsyncRelayCommand<InstallerService.WingetPackage> UpgradePackageCommand { get; }
    public IAsyncRelayCommand<InstallerService.WingetPackage> InstallPackageCommand { get; }
    public IAsyncRelayCommand<InstallerService.WingetPackage> UninstallPackageCommand { get; }
    public IAsyncRelayCommand InstallSelectedEssentialsCommand { get; }
    public IAsyncRelayCommand SearchCommand { get; }
    public IRelayCommand ClearLogCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public InstallerViewModel(InstallerService installerService)
    {
        _installerService = installerService;
        _loc = ServiceContainer.GetOptional<LocalizationService>();
        if (_loc != null)
        {
            _loc.LanguageChanged += newLang =>
            {
                OnPropertyChanged(nameof(SelectedEssentialsText));
                OnPropertyChanged(nameof(UpgradesCountText));
                if (OperationStatus == "Prêt" || OperationStatus == "Ready")
                {
                    OperationStatus = _loc.Get("Installer_Status_Ready");
                }
            };
            OperationStatus = _loc.Get("Installer_Status_Ready");
        }

        SelectTabCommand = new RelayCommand<string>(tab => { if (tab != null) { CurrentTab = tab; } });
        SelectCategoryCommand = new RelayCommand<string>(cat => { if (cat != null) { SelectedCategory = cat; } });
        SelectAllEssentialsCommand = new RelayCommand(() =>
        {
            foreach (InstallerService.WingetPackage p in FilteredEssentials)
            {
                p.IsSelected = true;
            }

            OnPropertyChanged(nameof(SelectedEssentialsCount));
            OnPropertyChanged(nameof(HasSelectedEssentials));
        });
        DeselectAllEssentialsCommand = new RelayCommand(() =>
        {
            foreach (InstallerService.WingetPackage p in FilteredEssentials)
            {
                p.IsSelected = false;
            }

            OnPropertyChanged(nameof(SelectedEssentialsCount));
            OnPropertyChanged(nameof(HasSelectedEssentials));
        });
        CheckUpgradesCommand = new AsyncRelayCommand(CheckUpgradesAsync);
        UpgradeAllCommand = new AsyncRelayCommand(UpgradeAllAsync);
        UpgradePackageCommand = new AsyncRelayCommand<InstallerService.WingetPackage>(UpgradePackageAsync);
        InstallPackageCommand = new AsyncRelayCommand<InstallerService.WingetPackage>(InstallPackageAsync);
        UninstallPackageCommand = new AsyncRelayCommand<InstallerService.WingetPackage>(UninstallPackageAsync);
        InstallSelectedEssentialsCommand = new AsyncRelayCommand(InstallSelectedEssentialsAsync);
        SearchCommand = new AsyncRelayCommand(SearchAsync);
        ClearLogCommand = new RelayCommand(OutputLog.Clear);
        CancelCommand = new RelayCommand(() => _cts?.Cancel());

        LoadEssentials();
        _ = CheckUpgradesAsync();

        _ = Task.Run(async () =>
        {
            try { await _installerService.DetectInstalledPackagesAsync(EssentialPackages); }
            catch (Exception ex) { LoggingService.LogException(ex, "InstallerViewModel.DetectInstalledPackagesAsync"); }
        });
    }

    private void LoadEssentials()
    {
        EssentialPackages.Clear();
        List<InstallerService.WingetPackage> list = _installerService.GetEssentialPackages();

        Categories.Clear();
        Categories.Add("Toutes");
        foreach (string cat in list.Select(p => p.Category).Distinct().OrderBy(c => c))
        {
            Categories.Add(cat);
        }

        foreach (InstallerService.WingetPackage item in list)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(InstallerService.WingetPackage.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedEssentialsCount));
                    OnPropertyChanged(nameof(HasSelectedEssentials));
                }
            };
            EssentialPackages.Add(item);
        }

        FilterEssentials();
    }

    private void FilterEssentials()
    {
        FilteredEssentials.Clear();
        IEnumerable<InstallerService.WingetPackage> items = SelectedCategory == "Toutes"
            ? EssentialPackages
            : EssentialPackages.Where(p => p.Category == SelectedCategory);

        foreach (InstallerService.WingetPackage item in items)
        {
            FilteredEssentials.Add(item);
        }
    }

    private void AppendLog(string text)
    {
        _ = (Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            OutputLog.Add($"[{DateTime.Now:HH:mm:ss}] {text}");
            if (OutputLog.Count > 250)
            {
                OutputLog.RemoveAt(0);
            }
        }));
    }

    public async Task CheckUpgradesAsync()
    {
        if (IsCheckingUpgrades)
        {
            return;
        }

        IsCheckingUpgrades = true;
        OperationStatus = _loc?.Get("Installer_Status_CheckingUpdates") ?? "Recherche des mises à jour logicielles...";
        _cts = new CancellationTokenSource();

        try
        {
            AppendLog(_loc?.Get("Installer_Status_CheckingWinget") ?? "Recherche des mises à jour disponibles via Winget...");
            List<InstallerService.WingetPackage> upgrades = await _installerService.GetAvailableUpgradesAsync(_cts.Token);
            AvailableUpgrades.Clear();
            foreach (InstallerService.WingetPackage u in upgrades)
            {
                AvailableUpgrades.Add(u);
            }

            OnPropertyChanged(nameof(UpgradesCount));
            OnPropertyChanged(nameof(HasUpgrades));
            OperationStatus = upgrades.Count > 0
                ? $"{upgrades.Count} mise(s) à jour logicielle(s) disponible(s)."
                : "Tous vos logiciels sont à jour !";
            AppendLog($"Résultat : {upgrades.Count} mise(s) à jour disponible(s).");
        }
        catch (OperationCanceledException)
        {
            OperationStatus = _loc?.Get("Installer_Status_CheckCancelled") ?? "Vérification annulée.";
        }
        catch (Exception ex)
        {
            OperationStatus = string.Format(_loc?.Get("Installer_Status_CheckError") ?? "Erreur vérification : {0}", ex.Message);
            LoggingService.LogException(ex, "InstallerVM.CheckUpgrades");
        }
        finally
        {
            IsCheckingUpgrades = false;
        }
    }

    public async Task UpgradeAllAsync()
    {
        if (IsOperating)
        {
            return;
        }

        if (AvailableUpgrades.Count == 0)
        {
            _ = MessageBox.Show(_loc?.Get("Installer_Dialog_NoUpdatesMsg") ?? "Aucune mise à jour détectée pour le moment.", _loc?.Get("Installer_Dialog_NoUpdatesTitle") ?? "Mises à jour", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            string.Format(_loc?.Get("Installer_Dialog_UpdateAllMsg") ?? "Voulez-vous mettre à jour TOUS les logiciels détectés ({0}) en arrière-plan ?", AvailableUpgrades.Count),
            _loc?.Get("Installer_Dialog_UpdateAllTitle") ?? "Confirmation de mise à jour globale",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        IsOperating = true;
        OperationStatus = "Mise à jour globale en cours...";
        _cts = new CancellationTokenSource();

        try
        {
            bool ok = await _installerService.UpgradeAllAsync(AppendLog, _cts.Token);
            OperationStatus = ok ? "Mise à jour globale terminée avec succès !" : "Mise à jour globale terminée avec des avertissements.";
            await CheckUpgradesAsync();
        }
        catch (OperationCanceledException)
        {
            OperationStatus = "Mise à jour interrompue.";
        }
        catch (Exception ex)
        {
            OperationStatus = string.Format(_loc?.Get("Installer_Status_SearchError") ?? "Erreur : {0}", ex.Message);
            AppendLog($"❌ Erreur mise à jour globale : {ex.Message}");
        }
        finally
        {
            IsOperating = false;
        }
    }

    public async Task UpgradePackageAsync(InstallerService.WingetPackage? package)
    {
        if (package == null || IsOperating)
        {
            return;
        }

        IsOperating = true;
        package.IsBusy = true;
        package.Status = _loc?.Get("Installer_Status_Updating") ?? "Mise à jour...";
        OperationStatus = string.Format(_loc?.Get("Installer_Status_UpdatingPkg") ?? "Mise à jour de {0}...", package.Name);
        _cts = new CancellationTokenSource();

        try
        {
            bool ok = await _installerService.UpgradePackageAsync(package.Id, AppendLog, _cts.Token);
            package.Status = ok ? "À jour" : "Erreur";
            if (ok)
            {
                _ = AvailableUpgrades.Remove(package);
            }

            OnPropertyChanged(nameof(UpgradesCount));
            OnPropertyChanged(nameof(HasUpgrades));
        }
        catch (Exception ex)
        {
            package.Status = "Erreur";
            AppendLog($"❌ Erreur mise à jour de {package.Name} : {ex.Message}");
        }
        finally
        {
            package.IsBusy = false;
            IsOperating = false;
        }
    }

    public async Task InstallPackageAsync(InstallerService.WingetPackage? package)
    {
        if (package == null || IsOperating)
        {
            return;
        }

        IsOperating = true;
        package.IsBusy = true;
        package.Status = _loc?.Get("Installer_Status_Installing") ?? "Installation...";
        OperationStatus = string.Format(_loc?.Get("Installer_Status_InstallingPkg") ?? "Installation de {0}...", package.Name);
        _cts = new CancellationTokenSource();

        try
        {
            bool ok = await _installerService.InstallPackageAsync(package.Id, AppendLog, _cts.Token);
            package.Status = ok ? "Installé" : "Erreur";
            if (ok)
            {
                package.IsInstalled = true;
            }
        }
        catch (Exception ex)
        {
            package.Status = "Erreur";
            AppendLog($"❌ Erreur installation de {package.Name} : {ex.Message}");
        }
        finally
        {
            package.IsBusy = false;
            IsOperating = false;
        }
    }

    public async Task UninstallPackageAsync(InstallerService.WingetPackage? package)
    {
        if (package == null || IsOperating)
        {
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            $"Désinstaller \"{package.Name}\" ({package.Id}) ?",
            "Confirmation de désinstallation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        IsOperating = true;
        package.IsBusy = true;
        package.Status = _loc?.Get("Installer_Status_Uninstalling") ?? "Désinstallation...";
        OperationStatus = string.Format(_loc?.Get("Installer_Status_UninstallingPkg") ?? "Désinstallation de {0}...", package.Name);
        _cts = new CancellationTokenSource();

        try
        {
            bool ok = await _installerService.UninstallPackageAsync(package.Id, AppendLog, _cts.Token);
            package.Status = ok ? "Désinstallé" : "Erreur";
            if (ok && AvailableUpgrades.Contains(package))
            {
                _ = AvailableUpgrades.Remove(package);
            }
        }
        finally
        {
            package.IsBusy = false;
            IsOperating = false;
        }
    }

    public async Task InstallSelectedEssentialsAsync()
    {
        var selected = EssentialPackages.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            _ = MessageBox.Show("Veuillez cocher au moins une application à installer.", "Catalogue Essentiels", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            string.Format(_loc?.Get("Installer_Dialog_BatchInstallMsg") ?? "Installer les {0} applications sélectionnées en séquence ?", selected.Count),
            _loc?.Get("Installer_Dialog_BatchInstallTitle") ?? "Installation par lot",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        IsOperating = true;
        _cts = new CancellationTokenSource();

        try
        {
            int index = 0;
            foreach (InstallerService.WingetPackage item in selected)
            {
                _cts.Token.ThrowIfCancellationRequested();
                index++;
                OperationStatus = $"[{index}/{selected.Count}] Installation de {item.Name}...";
                item.IsBusy = true;
                item.Status = "En cours...";

                bool ok = await _installerService.InstallPackageAsync(item.Id, AppendLog, _cts.Token);
                item.Status = ok ? "Installé" : "Erreur";
                if (ok)
                {
                    item.IsInstalled = true;
                }

                item.IsBusy = false;
            }

            OperationStatus = _loc?.Get("Installer_Status_BatchFinished") ?? "Toutes les installations sélectionnées sont terminées !";
        }
        catch (OperationCanceledException)
        {
            OperationStatus = _loc?.Get("Installer_Status_BatchCancelled") ?? "Installation par lot annulée.";
        }
        catch (Exception ex)
        {
            OperationStatus = $"Erreur lors de l'installation : {ex.Message}";
            AppendLog($"❌ Erreur lot : {ex.Message}");
        }
        finally
        {
            IsOperating = false;
        }
    }

    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) || IsSearching)
        {
            return;
        }

        IsSearching = true;
        SearchResults.Clear();
        OnPropertyChanged(nameof(HasSearchResults));
        OperationStatus = $"Recherche de \"{SearchQuery}\" dans le catalogue Winget...";
        _cts = new CancellationTokenSource();

        try
        {
            AppendLog($"Recherche universelle : \"{SearchQuery}\"...");
            string query = SearchQuery.Trim();
            List<InstallerService.WingetPackage> results = await _installerService.SearchWingetAsync(query, _cts.Token);

            var matchedEssentials = EssentialPackages
                .Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (InstallerService.WingetPackage ess in matchedEssentials)
            {
                if (!results.Any(r => r.Id.Equals(ess.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    results.Insert(0, new InstallerService.WingetPackage
                    {
                        Name = ess.Name,
                        Id = ess.Id,
                        Category = ess.Category,
                        Description = ess.Description,
                        Source = "Catalogue Essentiels"
                    });
                }
            }

            SearchResults.Clear();
            foreach (InstallerService.WingetPackage r in results)
            {
                SearchResults.Add(r);
            }

            OnPropertyChanged(nameof(HasSearchResults));

            OperationStatus = results.Count > 0
                ? $"{results.Count} application(s) trouvée(s)."
                : "Aucune application trouvée pour ce terme.";
            AppendLog($"Résultat de recherche : {results.Count} paquet(s) trouvé(s).");
        }
        catch (OperationCanceledException)
        {
            OperationStatus = _loc?.Get("Installer_Status_SearchCancelled") ?? "Recherche annulée.";
        }
        catch (Exception ex)
        {
            OperationStatus = string.Format(_loc?.Get("Installer_Status_SearchError") ?? "Erreur : {0}", ex.Message);
            LoggingService.LogException(ex, "InstallerVM.SearchAsync");
        }
        finally
        {
            IsSearching = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}