using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Coclico.Modules.Apps.Services;
using Coclico.Services;
using CommunityToolkit.Mvvm.Input;

namespace Coclico.ViewModels;

public class AppInstallItem : INotifyPropertyChanged
{
    public required AppCatalogService.AppCatalogEntry Entry { get; init; }

    public string Name => Entry.Name;
    public string Category => Entry.Category;
    public string Description => Entry.Description;
    public string Identifier => IsGithub ? Entry.Repo : Entry.WingetId;
    public bool IsGithub => string.Equals(Entry.Kind, "github", StringComparison.OrdinalIgnoreCase);

    public bool IsSelected
    {
        get;
        set { field = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = "Prêt";

    public bool IsBusy
    {
        get;
        set { field = value; OnPropertyChanged(); }
    }

    public bool IsInstalled
    {
        get;
        set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotInstalled)); }
    }

    public bool IsNotInstalled => !IsInstalled;

    public bool IsPackChecked
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = true;

    public bool IsFailed
    {
        get;
        set { field = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public class AppPackItem : INotifyPropertyChanged
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required List<AppInstallItem> Items { get; init; }

    public int TotalCount => Items.Count;

    public int InstalledCount
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AllInstalled));
                OnPropertyChanged(nameof(InstalledCountText));
            }
        }
    }

    public bool AllInstalled => InstalledCount >= Items.Count;

    public string InstalledCountText => $"{InstalledCount}/{Items.Count}";

    public bool IsBusy
    {
        get;
        set { field = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = string.Empty;

    public void RefreshInstalledCount()
    {
        InstalledCount = Items.Count(static i => i.IsInstalled);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public class UpgradeItem : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string AvailableVersion { get; init; }

    public bool IsBusy
    {
        get;
        set { field = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public partial class AppsViewModel : INotifyPropertyChanged
{
    private readonly AppCatalogService _catalog;
    private readonly WingetEngineService _winget;
    private readonly GitHubToolService _github;
    private readonly AppShortcutsService _shortcuts;
    private readonly LocalizationService? _loc;
    private CancellationTokenSource? _cts;

    public Func<string, string, bool>? Confirm { get; set; }
    public Action<string, string?>? Notify { get; set; }

    public ObservableCollection<AppInstallItem> AllApps { get; } = [];
    public ObservableCollection<AppInstallItem> FilteredApps { get; } = [];
    public ObservableCollection<string> Categories { get; } = [];
    public ObservableCollection<AppPackItem> Packs { get; } = [];
    public ObservableCollection<UpgradeItem> AvailableUpgrades { get; } = [];
    public ObservableCollection<WingetEngineService.WingetRow> SearchResults { get; } = [];
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
                OnPropertyChanged(nameof(IsTabInstaller));
                OnPropertyChanged(nameof(IsTabPacks));
                OnPropertyChanged(nameof(IsTabUpdates));
            }
        }
    } = "Installer";

    public bool IsTabInstaller => CurrentTab == "Installer";
    public bool IsTabPacks => CurrentTab == "Packs";
    public bool IsTabUpdates => CurrentTab == "Updates";

    public string SelectedCategory
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }
    } = "Toutes";

    public string SearchQuery
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }
    } = string.Empty;

    public string OnlineQuery
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = string.Empty;

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

    public bool IsNotBusy => !IsOperating && !IsCheckingUpgrades && !IsSearching;

    public bool IsWingetReady
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsWingetMissing));
            }
        }
    }

    public bool IsWingetMissing => !IsWingetReady;

    public string WingetStatusText
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = "WinGet : vérification...";

    public string OperationStatus
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = "Prêt";

    public double OverallProgress
    {
        get;
        set { field = value; OnPropertyChanged(); }
    }

    public int SelectedCount => AllApps.Count(static a => a.IsSelected);

    public string SelectedCountText => string.Format(_loc?.Get("Installer_SelectedCount") ?? "{0} sélectionné(s)", SelectedCount);

    public bool HasSelected => SelectedCount > 0;

    public int UpgradesCount => AvailableUpgrades.Count;
    public bool HasUpgrades => AvailableUpgrades.Count > 0;
    public string UpgradesCountText => string.Format(_loc?.Get("Installer_AvailableCount") ?? "{0} disponible(s)", UpgradesCount);
    public bool HasSearchResults => SearchResults.Count > 0;

    public AppsViewModel(AppCatalogService catalog, WingetEngineService winget, GitHubToolService github, AppShortcutsService shortcuts)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _winget = winget ?? throw new ArgumentNullException(nameof(winget));
        _github = github ?? throw new ArgumentNullException(nameof(github));
        _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        _loc = ServiceContainer.GetOptional<LocalizationService>();

        if (_loc != null)
        {
            _loc.LanguageChanged += _ =>
            {
                OnPropertyChanged(nameof(SelectedCountText));
                OnPropertyChanged(nameof(UpgradesCountText));
                UpdateWingetStatusText();
            };
        }

        LoadCatalog();
        LoadPacks();
        _ = CheckWingetAsync();
        _ = Task.Run(async () =>
        {
            try { await DetectInstalledAsync(); }
            catch (Exception ex) { LoggingService.LogException(ex, "AppsViewModel.DetectInstalledAsync"); }
        });
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                if (await _winget.IsWingetAvailableAsync().ConfigureAwait(false))
                {
                    await CheckUpgradesCoreAsync(autoScan: true).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { LoggingService.LogException(ex, "AppsViewModel.AutoScanUpgrades"); }
        });
    }

    private string L(string key, string fallback)
    {
        return _loc?.Get(key) ?? fallback;
    }

    private void LoadCatalog()
    {
        AllApps.Clear();
        foreach (AppCatalogService.AppCatalogEntry entry in _catalog.GetApps())
        {
            var item = new AppInstallItem { Entry = entry };
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppInstallItem.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedCount));
                    OnPropertyChanged(nameof(SelectedCountText));
                    OnPropertyChanged(nameof(HasSelected));
                }
            };
            AllApps.Add(item);
        }

        Categories.Clear();
        Categories.Add("Toutes");
        foreach (string category in _catalog.GetCategories())
        {
            Categories.Add(category);
        }

        ApplyFilter();
    }

    private void LoadPacks()
    {
        Packs.Clear();
        foreach (AppCatalogService.AppPackDefinition definition in _catalog.GetPacks())
        {
            var items = new List<AppInstallItem>();
            foreach (string appId in definition.AppIds)
            {
                AppInstallItem? item = AllApps.FirstOrDefault(a => string.Equals(a.Entry.Id, appId, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    items.Add(item);
                }
            }

            if (items.Count == 0)
            {
                continue;
            }

            var pack = new AppPackItem
            {
                Id = definition.Id,
                Name = definition.Name,
                Description = definition.Description,
                Items = items
            };

            foreach (AppInstallItem item in items)
            {
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(AppInstallItem.IsInstalled))
                    {
                        pack.RefreshInstalledCount();
                    }
                };
            }

            pack.RefreshInstalledCount();
            Packs.Add(pack);
        }
    }

    private void ApplyFilter()
    {
        FilteredApps.Clear();
        string query = SearchQuery.Trim();
        foreach (AppInstallItem item in AllApps)
        {
            bool categoryMatch = SelectedCategory == "Toutes" || item.Category == SelectedCategory;
            bool queryMatch = query.Length == 0
                || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Identifier.Contains(query, StringComparison.OrdinalIgnoreCase);

            if (categoryMatch && queryMatch)
            {
                FilteredApps.Add(item);
            }
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

    private void UpdateWingetStatusText()
    {
        WingetStatusText = IsWingetReady
            ? L("Apps_WingetReady", "WinGet prêt")
            : L("Apps_WingetMissing", "WinGet absent");
    }

    public async Task CheckWingetAsync()
    {
        IsWingetReady = await _winget.IsWingetAvailableAsync().ConfigureAwait(true);
        UpdateWingetStatusText();
        if (!IsWingetReady)
        {
            AppendLog(L("Apps_WingetMissing", "WinGet absent"));
        }
    }

    public async Task DetectInstalledAsync()
    {
        HashSet<string> installedIds = await _winget.DetectInstalledIdsAsync().ConfigureAwait(false);
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            foreach (AppInstallItem item in AllApps)
            {
                if (item.IsGithub)
                {
                    if (Directory.Exists(GitHubToolService.GetToolDir(item.Name)))
                    {
                        item.IsInstalled = true;
                    }
                }
                else if (installedIds.Contains(item.Entry.WingetId))
                {
                    item.IsInstalled = true;
                }
            }

            foreach (AppPackItem pack in Packs)
            {
                pack.RefreshInstalledCount();
            }
        });
    }

    [RelayCommand]
    private void SelectTab(string? tab)
    {
        if (tab != null)
        {
            CurrentTab = tab;
        }
    }

    [RelayCommand]
    private void SelectCategory(string? category)
    {
        if (category != null)
        {
            SelectedCategory = category;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (AppInstallItem item in FilteredApps)
        {
            item.IsSelected = true;
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(HasSelected));
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (AppInstallItem item in AllApps)
        {
            item.IsSelected = false;
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(HasSelected));
    }

    [RelayCommand]
    private void ClearLog() => OutputLog.Clear();

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task RepairWingetAsync()
    {
        if (IsOperating)
        {
            return;
        }

        IsOperating = true;
        OperationStatus = "Réparation de WinGet...";
        var progress = new Progress<string>(AppendLog);
        try
        {
            bool ok = await _winget.EnsureWingetAsync(progress).ConfigureAwait(true);
            IsWingetReady = ok;
            UpdateWingetStatusText();
            OperationStatus = ok ? "WinGet est opérationnel." : "Impossible d'installer WinGet.";
            Notify?.Invoke(OperationStatus, "WinGet");
            AppendLog(OperationStatus);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AppsViewModel.RepairWingetAsync");
            OperationStatus = $"Erreur WinGet : {ex.Message}";
        }
        finally
        {
            IsOperating = false;
        }
    }

    [RelayCommand]
    private async Task InstallSelectedAsync()
    {
        if (IsOperating)
        {
            return;
        }

        var selected = AllApps.Where(static a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            Notify?.Invoke(L("Packs_NothingSelected", "Aucun élément sélectionné."), "Coclico");
            return;
        }

        bool confirmed = Confirm?.Invoke(
            string.Format(L("Installer_Dialog_BatchInstallMsg", "Installer les {0} applications sélectionnées en séquence ?"), selected.Count),
            L("Installer_Dialog_BatchInstallTitle", "Installation par lot")) ?? false;
        if (!confirmed)
        {
            return;
        }

        await RunInstallBatchAsync(selected).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task InstallOneAsync(AppInstallItem? item)
    {
        if (item == null || IsOperating)
        {
            return;
        }

        if (item.IsGithub)
        {
            await RunGithubBatchAsync([item]).ConfigureAwait(true);
            return;
        }

        await RunInstallBatchAsync([item]).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task InstallPackAsync(AppPackItem? pack)
    {
        if (pack == null || IsOperating)
        {
            return;
        }

        var toInstall = pack.Items.Where(static i => i.IsPackChecked && !i.IsInstalled).ToList();
        if (toInstall.Count == 0)
        {
            Notify?.Invoke(L("Packs_NothingSelected", "Aucun élément sélectionné — coche au moins un outil."), pack.Name);
            return;
        }

        bool confirmed = Confirm?.Invoke(
            string.Format(L("Installer_Dialog_BatchInstallMsg", "Installer les {0} applications sélectionnées en séquence ?"), toInstall.Count),
            pack.Name) ?? false;
        if (!confirmed)
        {
            return;
        }

        pack.IsBusy = true;
        pack.Status = L("Packs_BtnInstall", "Installer") + "...";
        try
        {
            await RunInstallBatchAsync(toInstall).ConfigureAwait(true);
        }
        finally
        {
            pack.IsBusy = false;
            pack.Status = string.Empty;
        }
    }

    [RelayCommand]
    private async Task UninstallOneAsync(AppInstallItem? item)
    {
        if (item == null || IsOperating || item.IsGithub || !item.IsInstalled)
        {
            return;
        }

        bool confirmed = Confirm?.Invoke(
            string.Format("Désinstaller {0} ?", item.Name),
            "Désinstallation") ?? false;
        if (!confirmed)
        {
            return;
        }

        IsOperating = true;
        OperationStatus = string.Format(L("Installer_Status_UninstallingPkg", "Désinstallation de {0}..."), item.Name);
        _cts = new CancellationTokenSource();
        item.IsBusy = true;
        item.Status = "Désinstallation...";

        try
        {
            WingetEngineService.InstallResult result = await _winget.UninstallAsync(item.Entry.WingetId,
                new Progress<string>(AppendLog), _cts.Token).ConfigureAwait(true);
            if (result.Status is WingetEngineService.InstallStatus.Succeeded
                or WingetEngineService.InstallStatus.RestartRequired
                or WingetEngineService.InstallStatus.AlreadyUpToDate
                or WingetEngineService.InstallStatus.AlreadyInstalled)
            {
                item.IsInstalled = false;
                item.Status = "Désinstallé";
                OperationStatus = $"{item.Name} désinstallé.";
            }
            else
            {
                item.IsFailed = true;
                item.Status = $"Échec (code {result.ExitCode})";
                OperationStatus = $"Échec de la désinstallation de {item.Name}.";
            }

            AppendLog(OperationStatus);
        }
        catch (OperationCanceledException)
        {
            item.Status = "Annulé";
            OperationStatus = L("Installer_Status_BatchCancelled", "Opération annulée.");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AppsViewModel.UninstallOneAsync");
            item.IsFailed = true;
            item.Status = $"Erreur : {ex.Message}";
            OperationStatus = item.Status;
        }
        finally
        {
            item.IsBusy = false;
            IsOperating = false;
        }
    }

    [RelayCommand]
    private Task CheckUpgradesAsync()
    {
        return CheckUpgradesCoreAsync(autoScan: false);
    }

    private async Task CheckUpgradesCoreAsync(bool autoScan)
    {
        if (IsCheckingUpgrades)
        {
            return;
        }

        IsCheckingUpgrades = true;
        OperationStatus = L("Installer_Status_CheckingUpdates", "Recherche des mises à jour logicielles...");
        _cts = new CancellationTokenSource();
        AppendLog(L("Installer_Status_CheckingWinget", "Recherche des mises à jour disponibles via Winget..."));

        try
        {
            List<WingetEngineService.WingetRow> upgrades = await _winget.GetAvailableUpgradesAsync(_cts.Token).ConfigureAwait(true);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                AvailableUpgrades.Clear();
                foreach (WingetEngineService.WingetRow upgrade in upgrades)
                {
                    AvailableUpgrades.Add(new UpgradeItem
                    {
                        Name = upgrade.Name,
                        Id = upgrade.Id,
                        Version = upgrade.Version,
                        AvailableVersion = upgrade.AvailableVersion
                    });
                }

                OnPropertyChanged(nameof(UpgradesCount));
                OnPropertyChanged(nameof(HasUpgrades));
                OnPropertyChanged(nameof(UpgradesCountText));
            });

            OperationStatus = upgrades.Count > 0
                ? $"{upgrades.Count} mise(s) à jour logicielle(s) disponible(s)."
                : L("Installer_AllUpToDate", "Tous vos logiciels sont à jour !");
            AppendLog($"Résultat : {upgrades.Count} mise(s) à jour disponible(s).");

            if (autoScan && upgrades.Count > 0)
            {
                Notify?.Invoke($"{upgrades.Count} mise(s) à jour d'applications disponible(s) — onglet Mises à jour.", "Coclico");
            }
        }
        catch (OperationCanceledException)
        {
            OperationStatus = L("Installer_Status_CheckCancelled", "Vérification annulée.");
        }
        catch (Exception ex)
        {
            OperationStatus = string.Format(L("Installer_Status_CheckError", "Erreur vérification : {0}"), ex.Message);
            LoggingService.LogException(ex, "AppsViewModel.CheckUpgradesAsync");
        }
        finally
        {
            IsCheckingUpgrades = false;
        }
    }

    [RelayCommand]
    private async Task UpgradeOneAsync(UpgradeItem? item)
    {
        if (item == null || IsOperating)
        {
            return;
        }

        IsOperating = true;
        _cts = new CancellationTokenSource();
        item.IsBusy = true;
        item.Status = L("Installer_Status_Updating", "Mise à jour...");
        OperationStatus = string.Format(L("Installer_Status_UpdatingPkg", "Mise à jour de {0}..."), item.Name);
        AppendLog(OperationStatus);

        try
        {
            WingetEngineService.InstallResult result = await _winget.UpgradeAsync(item.Id,
                new Progress<string>(AppendLog), _cts.Token).ConfigureAwait(true);
            if (result.Status is WingetEngineService.InstallStatus.Succeeded
                or WingetEngineService.InstallStatus.RestartRequired
                or WingetEngineService.InstallStatus.AlreadyUpToDate
                or WingetEngineService.InstallStatus.AlreadyInstalled)
            {
                item.Status = "À jour";
                AppendLog($"{item.Name} : mise à jour terminée.");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _ = AvailableUpgrades.Remove(item);
                    OnPropertyChanged(nameof(UpgradesCount));
                    OnPropertyChanged(nameof(HasUpgrades));
                    OnPropertyChanged(nameof(UpgradesCountText));
                });
                OperationStatus = $"{item.Name} est à jour.";
            }
            else
            {
                item.Status = result.Status == WingetEngineService.InstallStatus.AdminRequired
                    ? "Droits administrateur requis"
                    : $"Échec (code {result.ExitCode})";
                OperationStatus = $"{item.Name} : {item.Status}";
                AppendLog(OperationStatus);
            }
        }
        catch (OperationCanceledException)
        {
            item.Status = "Annulé";
            OperationStatus = L("Installer_Status_BatchCancelled", "Opération annulée.");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AppsViewModel.UpgradeOneAsync");
            item.Status = $"Erreur : {ex.Message}";
            OperationStatus = item.Status;
        }
        finally
        {
            item.IsBusy = false;
            IsOperating = false;
        }
    }

    [RelayCommand]
    private async Task UpgradeAllAsync()
    {
        if (IsOperating)
        {
            return;
        }

        if (AvailableUpgrades.Count == 0)
        {
            Notify?.Invoke(
                L("Installer_Dialog_NoUpdatesMsg", "Aucune mise à jour détectée pour le moment."),
                L("Installer_Dialog_NoUpdatesTitle", "Mises à jour"));
            return;
        }

        bool confirmed = Confirm?.Invoke(
            string.Format(L("Installer_Dialog_UpdateAllMsg", "Mettre à jour les {0} applications détectées ?"), AvailableUpgrades.Count),
            L("Installer_Dialog_UpdateAllTitle", "Mise à jour globale")) ?? false;
        if (!confirmed)
        {
            return;
        }

        IsOperating = true;
        _cts = new CancellationTokenSource();
        OverallProgress = 0;
        var progress = new Progress<string>(AppendLog);
        List<UpgradeItem> items = [.. AvailableUpgrades];
        int done = 0;
        int failed = 0;
        OperationStatus = "Mise à jour globale en cours...";

        try
        {
            foreach (UpgradeItem item in items)
            {
                _cts.Token.ThrowIfCancellationRequested();
                item.IsBusy = true;
                item.Status = L("Installer_Status_Updating", "Mise à jour...");
                AppendLog(string.Format(L("Installer_Status_UpdatingPkg", "Mise à jour de {0}..."), item.Name));

                WingetEngineService.InstallResult result = await _winget.UpgradeAsync(item.Id, progress, _cts.Token).ConfigureAwait(true);
                if (result.Status is WingetEngineService.InstallStatus.Succeeded
                    or WingetEngineService.InstallStatus.RestartRequired
                    or WingetEngineService.InstallStatus.AlreadyUpToDate
                    or WingetEngineService.InstallStatus.AlreadyInstalled)
                {
                    item.Status = "À jour";
                    await Application.Current.Dispatcher.InvokeAsync(() => _ = AvailableUpgrades.Remove(item));
                }
                else
                {
                    item.Status = result.Status == WingetEngineService.InstallStatus.AdminRequired
                        ? "Droits administrateur requis"
                        : $"Échec (code {result.ExitCode})";
                    failed++;
                }

                item.IsBusy = false;
                done++;
                OverallProgress = 100.0 * done / items.Count;
                AppendLog($"{item.Name} : {item.Status}");
            }

            OnPropertyChanged(nameof(UpgradesCount));
            OnPropertyChanged(nameof(HasUpgrades));
            OnPropertyChanged(nameof(UpgradesCountText));
            OperationStatus = failed == 0
                ? "Mise à jour globale terminée avec succès."
                : $"Mise à jour globale terminée avec {failed} échec(s).";
            Notify?.Invoke(OperationStatus, "Coclico");
            AppendLog(OperationStatus);
        }
        catch (OperationCanceledException)
        {
            OperationStatus = L("Installer_Status_BatchCancelled", "Opération annulée.");
            AppendLog(OperationStatus);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AppsViewModel.UpgradeAllAsync");
            OperationStatus = $"Erreur : {ex.Message}";
            AppendLog(OperationStatus);
        }
        finally
        {
            foreach (UpgradeItem item in items)
            {
                item.IsBusy = false;
            }

            IsOperating = false;
        }
    }

    [RelayCommand]
    private async Task OnlineSearchAsync()
    {
        string query = OnlineQuery.Trim();
        if (query.Length == 0 || IsSearching)
        {
            return;
        }

        IsSearching = true;
        _cts = new CancellationTokenSource();
        OperationStatus = L("Installer_SearchingWinget", "Recherche en direct dans Winget...");
        AppendLog(OperationStatus);

        try
        {
            List<WingetEngineService.WingetRow> results = await _winget.SearchAsync(query, _cts.Token).ConfigureAwait(true);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SearchResults.Clear();
                foreach (WingetEngineService.WingetRow row in results)
                {
                    SearchResults.Add(row);
                }

                OnPropertyChanged(nameof(HasSearchResults));
            });

            OperationStatus = $"{results.Count} résultat(s) pour « {query} ».";
            AppendLog(OperationStatus);
        }
        catch (OperationCanceledException)
        {
            OperationStatus = L("Installer_Status_SearchCancelled", "Recherche annulée.");
        }
        catch (Exception ex)
        {
            OperationStatus = string.Format(L("Installer_Status_SearchError", "Erreur : {0}"), ex.Message);
            LoggingService.LogException(ex, "AppsViewModel.OnlineSearchAsync");
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task InstallSearchResultAsync(WingetEngineService.WingetRow? row)
    {
        if (row == null || IsOperating)
        {
            return;
        }

        IsOperating = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(AppendLog);
        OperationStatus = string.Format(L("Installer_Status_InstallingPkg", "Installation de {0}..."), row.Name);
        AppendLog(OperationStatus);

        try
        {
            if (!await _winget.IsWingetAvailableAsync().ConfigureAwait(true))
            {
                AppendLog("WinGet absent — vérification de l'App Installer...");
                bool ready = await _winget.EnsureWingetAsync(progress, _cts.Token).ConfigureAwait(true);
                IsWingetReady = ready;
                UpdateWingetStatusText();
                if (!ready)
                {
                    Notify?.Invoke("Impossible d'installer ou réparer WinGet.", "WinGet");
                    OperationStatus = "WinGet indisponible.";
                    return;
                }
            }

            WingetEngineService.InstallResult result = await _winget.InstallAsync(row.Id, progress, _cts.Token).ConfigureAwait(true);
            if (result.Status is WingetEngineService.InstallStatus.Succeeded
                or WingetEngineService.InstallStatus.RestartRequired
                or WingetEngineService.InstallStatus.AlreadyUpToDate
                or WingetEngineService.InstallStatus.AlreadyInstalled)
            {
                OperationStatus = $"{row.Name} installé.";
                _ = await Task.Run(() => _shortcuts.CopyStartMenuShortcuts([row.Name], progress)).ConfigureAwait(true);
            }
            else
            {
                OperationStatus = result.Status == WingetEngineService.InstallStatus.AdminRequired
                    ? $"{row.Name} : droits administrateur requis."
                    : $"{row.Name} : échec (code {result.ExitCode}).";
            }

            AppendLog(OperationStatus);
        }
        catch (OperationCanceledException)
        {
            OperationStatus = L("Installer_Status_BatchCancelled", "Opération annulée.");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AppsViewModel.InstallSearchResultAsync");
            OperationStatus = $"Erreur : {ex.Message}";
            AppendLog(OperationStatus);
        }
        finally
        {
            IsOperating = false;
        }
    }

    private async Task RunInstallBatchAsync(List<AppInstallItem> selected)
    {
        IsOperating = true;
        _cts = new CancellationTokenSource();
        OverallProgress = 0;
        var wingetItems = selected.Where(static a => !a.IsGithub).ToList();
        var githubItems = selected.Where(static a => a.IsGithub).ToList();
        int total = selected.Count;
        int done = 0;
        var progress = new Progress<string>(AppendLog);

        try
        {
            if (wingetItems.Count > 0)
            {
                if (!await _winget.IsWingetAvailableAsync().ConfigureAwait(true))
                {
                    AppendLog(L("Apps_WingetMissing", "WinGet absent") + " — vérification de l'App Installer...");
                    OperationStatus = "Vérification de l'App Installer...";
                    bool ready = await _winget.EnsureWingetAsync(progress, _cts.Token).ConfigureAwait(true);
                    IsWingetReady = ready;
                    UpdateWingetStatusText();
                    if (!ready)
                    {
                        Notify?.Invoke("Impossible d'installer ou réparer WinGet. Les applications WinGet ne peuvent pas être installées.", "WinGet");
                        OperationStatus = "WinGet indisponible.";
                        return;
                    }
                }

                int failed = 0;
                foreach (AppInstallItem item in wingetItems)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    item.IsBusy = true;
                    item.IsFailed = false;
                    item.Status = "Installation...";
                    OperationStatus = string.Format(L("Installer_Status_InstallingPkg", "Installation de {0}..."), item.Name);
                    AppendLog(OperationStatus);

                    WingetEngineService.InstallResult result = await _winget.InstallAsync(item.Entry.WingetId, progress, _cts.Token).ConfigureAwait(true);
                    switch (result.Status)
                    {
                        case WingetEngineService.InstallStatus.Succeeded:
                            item.IsInstalled = true;
                            item.Status = "Installé";
                            break;
                        case WingetEngineService.InstallStatus.RestartRequired:
                            item.IsInstalled = true;
                            item.Status = "Installé (redémarrage requis)";
                            break;
                        case WingetEngineService.InstallStatus.AlreadyUpToDate:
                        case WingetEngineService.InstallStatus.AlreadyInstalled:
                            item.IsInstalled = true;
                            item.Status = L("Installer_AlreadyInstalled", "Déjà installé");
                            break;
                        case WingetEngineService.InstallStatus.AdminRequired:
                            item.IsFailed = true;
                            item.Status = "Droits administrateur requis";
                            failed++;
                            break;
                        case WingetEngineService.InstallStatus.Busy:
                            item.IsFailed = true;
                            item.Status = "Windows Installer occupé";
                            failed++;
                            break;
                        default:
                            item.IsFailed = true;
                            item.Status = $"Échec (code {result.ExitCode})";
                            failed++;
                            break;
                    }

                    item.IsBusy = false;
                    done++;
                    OverallProgress = 100.0 * done / total;
                    AppendLog($"{item.Name} : {item.Status}");
                }

                var installedNames = wingetItems.Where(static i => i.IsInstalled).Select(static i => i.Name).ToList();
                if (installedNames.Count > 0)
                {
                    OperationStatus = "Copie des raccourcis...";
                    _ = await Task.Run(() => _shortcuts.CopyStartMenuShortcuts(installedNames, progress)).ConfigureAwait(true);
                }

                AppendLog($"{wingetItems.Count - failed} réussi(s), {failed} échec(s) sur les applications WinGet.");
            }

            if (githubItems.Count > 0)
            {
                await InstallGithubItemsAsync(githubItems, progress, _cts.Token, done, total).ConfigureAwait(true);
            }

            OperationStatus = L("Installer_Status_BatchFinished", "Toutes les installations sélectionnées sont terminées !");
            Notify?.Invoke(OperationStatus, "Coclico");
            AppendLog(OperationStatus);

            _ = Task.Run(async () =>
            {
                try { await DetectInstalledAsync(); }
                catch (Exception ex) { LoggingService.LogException(ex, "AppsViewModel.DetectInstalledAsync"); }
            });
        }
        catch (OperationCanceledException)
        {
            OperationStatus = L("Installer_Status_BatchCancelled", "Installation par lot annulée.");
            AppendLog(OperationStatus);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AppsViewModel.RunInstallBatchAsync");
            OperationStatus = $"Erreur : {ex.Message}";
            AppendLog(OperationStatus);
        }
        finally
        {
            foreach (AppInstallItem item in selected)
            {
                item.IsBusy = false;
            }

            IsOperating = false;
        }
    }

    private async Task RunGithubBatchAsync(List<AppInstallItem> items)
    {
        IsOperating = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(AppendLog);
        await InstallGithubItemsAsync(items, progress, _cts.Token, 0, items.Count).ConfigureAwait(true);
        IsOperating = false;
    }

    private async Task InstallGithubItemsAsync(List<AppInstallItem> items, IProgress<string> progress, CancellationToken ct, int doneOffset, int total)
    {
        foreach (AppInstallItem item in items)
        {
            ct.ThrowIfCancellationRequested();
            item.IsBusy = true;
            item.IsFailed = false;
            item.Status = "Téléchargement...";
            OperationStatus = string.Format(L("Installer_Status_InstallingPkg", "Téléchargement de {0}..."), item.Name);
            AppendLog(OperationStatus);

            GitHubToolService.GithubToolResult result = await _github.InstallToolAsync(item.Name, item.Entry.Repo, item.Entry.AssetPattern, progress, ct).ConfigureAwait(true);
            if (result.Success)
            {
                item.IsInstalled = true;
                item.Status = "Installé dans Documents";
                if (result.ExePath != null)
                {
                    await _github.CreateShortcutForExeAsync(item.Name, result.ExePath, ct).ConfigureAwait(true);
                    AppendLog($"Raccourci créé pour {Path.GetFileName(result.ExePath)}");
                }
            }
            else
            {
                item.IsFailed = true;
                item.Status = $"Échec : {result.Error}";
            }

            item.IsBusy = false;
            doneOffset++;
            if (total > 0)
            {
                OverallProgress = 100.0 * doneOffset / total;
            }

            AppendLog($"{item.Name} : {item.Status}");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
