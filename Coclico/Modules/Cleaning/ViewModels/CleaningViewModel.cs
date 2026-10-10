using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Coclico.Services;
using CommunityToolkit.Mvvm.Input;

namespace Coclico.ViewModels;

public partial class CleaningViewModel : INotifyPropertyChanged
{
    private readonly CleaningService _cleaningService;
    private readonly LocalizationService? _loc;
    private CancellationTokenSource? _cts;
    private bool _hasCompletedScan;
    private bool _isBulkUpdatingSelection;

    public ObservableCollection<CleaningService.CleaningCategory> Categories { get; } = [];

    public bool IsCleaning
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotBusy));
                OnPropertyChanged(nameof(CleanButtonText));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsScanning
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

    public bool IsNotBusy => !IsCleaning && !IsScanning;

    public string StatusMessage
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = string.Empty;

    public int Progress
    {
        get;
        set { field = value; OnPropertyChanged(); }
    }

    public string CurrentPreset
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPresetQuick));
                OnPropertyChanged(nameof(IsPresetRecommended));
                OnPropertyChanged(nameof(IsPresetDeep));
                OnPropertyChanged(nameof(IsPresetCustom));
            }
        }
    } = "Recommended";

    public bool IsPresetQuick => CurrentPreset == "Quick";
    public bool IsPresetRecommended => CurrentPreset == "Recommended";
    public bool IsPresetDeep => CurrentPreset == "Deep";
    public bool IsPresetCustom => CurrentPreset == "Custom";

    public long TotalCleanableBytes => Categories.Sum(c => c.BytesFound);
    public string TotalCleanableText => CleaningService.FormatSize(TotalCleanableBytes);

    public long SelectedCleanableBytes => Categories.Where(c => c.IsSelected).Sum(c => c.BytesFound);
    public string SelectedCleanableText => CleaningService.FormatSize(SelectedCleanableBytes);

    public string CleanButtonText
    {
        get
        {
            if (IsCleaning)
            {
                return _loc?.Get("Cleaning_Btn_Cleaning") ?? "Nettoyage en cours...";
            }
            if (SelectedCleanableBytes > 0)
            {
                string fmt = _loc?.Get("Cleaning_Btn_CleanSelectionWithTotal") ?? "Nettoyer la sélection ({0})";
                return string.Format(fmt, SelectedCleanableText);
            }
            return _loc?.Get("Cleaning_Btn_CleanSelection") ?? "Nettoyer la sélection";
        }
    }

    public IAsyncRelayCommand ScanCommand { get; }
    public IAsyncRelayCommand CleanCommand { get; }
    public IRelayCommand<string> SelectPresetCommand { get; }
    public IRelayCommand SelectAllCommand { get; }
    public IRelayCommand DeselectAllCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public CleaningViewModel(CleaningService cleaningService, IDialogService dialogService)
    {
        _cleaningService = cleaningService;
        _loc = ServiceContainer.GetOptional<LocalizationService>();
        StatusMessage = _loc?.Get("Cleaning_Status_Ready") ?? "Prêt pour l'analyse";

        ScanCommand = new AsyncRelayCommand(ScanAsync);
        CleanCommand = new AsyncRelayCommand(CleanAsync);
        SelectPresetCommand = new RelayCommand<string>(ApplyPreset);
        SelectAllCommand = new RelayCommand(() => SetAllSelection(true));
        DeselectAllCommand = new RelayCommand(() => SetAllSelection(false));
        CancelCommand = new RelayCommand(() => _cts?.Cancel());

        LoadCategories();
    }

    public void AttachLanguageChanged()
    {
        if (_loc != null)
        {
            _loc.LanguageChanged -= OnLanguageChanged;
            _loc.LanguageChanged += OnLanguageChanged;
        }
    }

    public void DetachLanguageChanged()
    {
        if (_loc != null)
        {
            _loc.LanguageChanged -= OnLanguageChanged;
        }
    }

    private void OnLanguageChanged(string? newLang)
    {
        foreach (var cat in Categories)
        {
            cat.NotifyLanguageChanged();
        }
        UpdateSizes();
        if (!IsCleaning && !IsScanning)
        {
            if (_hasCompletedScan)
            {
                StatusMessage = string.Format(_loc?.Get("Cleaning_Status_ScanCompleted") ?? "Analyse terminée : {0} d'espace libérable détecté.", TotalCleanableText);
            }
            else
            {
                StatusMessage = _loc?.Get("Cleaning_Status_Ready") ?? "Prêt pour l'analyse";
            }
        }
    }

    private void LoadCategories()
    {
        Categories.Clear();
        List<CleaningService.CleaningCategory> available = _cleaningService.GetAvailableCategories();
        foreach (CleaningService.CleaningCategory category in available)
        {
            category.PropertyChanged += OnCategoryPropertyChanged;
            Categories.Add(category);
        }
        _cleaningService.ApplyPreset(Categories, CurrentPreset);
        UpdateSizes();
    }

    private void OnCategoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isBulkUpdatingSelection && e.PropertyName == nameof(CleaningService.CleaningCategory.IsSelected))
        {
            UpdateSizes();
        }
    }

    private void UpdateSizes()
    {
        OnPropertyChanged(nameof(TotalCleanableBytes));
        OnPropertyChanged(nameof(TotalCleanableText));
        OnPropertyChanged(nameof(SelectedCleanableBytes));
        OnPropertyChanged(nameof(SelectedCleanableText));
        OnPropertyChanged(nameof(CleanButtonText));
    }

    private void ApplyPreset(string? preset)
    {
        if (string.IsNullOrEmpty(preset))
        {
            return;
        }

        CurrentPreset = preset;
        _isBulkUpdatingSelection = true;
        try
        {
            _cleaningService.ApplyPreset(Categories, preset);
        }
        finally
        {
            _isBulkUpdatingSelection = false;
        }
        UpdateSizes();
    }

    private void SetAllSelection(bool isSelected)
    {
        CurrentPreset = "Custom";
        _isBulkUpdatingSelection = true;
        try
        {
            foreach (CleaningService.CleaningCategory cat in Categories)
            {
                cat.IsSelected = isSelected;
            }
        }
        finally
        {
            _isBulkUpdatingSelection = false;
        }
        UpdateSizes();
    }

    public async Task ScanAsync()
    {
        if (IsScanning || IsCleaning)
        {
            return;
        }

        IsScanning = true;
        StatusMessage = _loc?.Get("Cleaning_Status_Scanning") ?? "🔍 Analyse des fichiers temporaires, caches et résidus...";
        _cts = new CancellationTokenSource();

        try
        {
            await _cleaningService.ScanAllCategoriesAsync(Categories, _cts.Token);
            UpdateSizes();
            _hasCompletedScan = true;
            StatusMessage = string.Format(_loc?.Get("Cleaning_Status_ScanCompleted") ?? "Analyse terminée : {0} d'espace libérable détecté.", TotalCleanableText);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _loc?.Get("Cleaning_Status_Cancelled") ?? "Analyse annulée.";
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(_loc?.Get("Cleaning_Status_Error") ?? "Erreur lors de l'analyse : {0}", ex.Message);
            LoggingService.LogException(ex, "CleaningVM.ScanAsync");
        }
        finally
        {
            IsScanning = false;
        }
    }

    public async Task CleanAsync()
    {
        if (IsCleaning || IsScanning)
        {
            return;
        }

        var selected = Categories.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0)
        {
            string msg = _loc?.Get("Cleaning_Dialog_SelectAtLeastOne") ?? "Veuillez sélectionner au moins une catégorie à nettoyer.";
            string title = _loc?.Get("Cleaning_Title") ?? "Nettoyage Système";
            _ = MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string confirmMsg = string.Format(
            _loc?.Get("Cleaning_Dialog_ConfirmMsg") ?? "Voulez-vous supprimer les fichiers sélectionnés ?\n\nEstimation : {0} répartis sur {1} catégories.",
            SelectedCleanableText, selected.Count);
        string confirmTitle = _loc?.Get("Cleaning_ConfirmTitle") ?? "Confirmation de nettoyage";

        MessageBoxResult confirm = MessageBox.Show(
            confirmMsg,
            confirmTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        IsCleaning = true;
        Progress = 0;
        StatusMessage = _loc?.Get("Cleaning_Status_CleaningStart") ?? "Démarrage du nettoyage sécurisé...";
        _cts = new CancellationTokenSource();

        var progressReporter = new Progress<(string status, int percent, long bytesFreed)>(p =>
        {
            Progress = p.percent;
            StatusMessage = $"{p.status} ({p.percent}%)";
        });

        try
        {
            CleaningService.CleaningResult result = await _cleaningService.ExecuteDeepCleanAsync(selected, progressReporter, _cts.Token);
            Progress = 100;
            StatusMessage = string.Format(_loc?.Get("Cleaning_Status_CleanCompleted") ?? "✅ Nettoyage terminé ! {0} libérés ({1} fichiers supprimés).", CleaningService.FormatSize(result.TotalBytesFreed), result.FilesDeleted);

            await _cleaningService.ScanAllCategoriesAsync(Categories, CancellationToken.None);
            UpdateSizes();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _loc?.Get("Cleaning_Status_Cancelled") ?? "Nettoyage interrompu par l'utilisateur.";
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(_loc?.Get("Cleaning_Status_Error") ?? "Erreur lors du nettoyage : {0}", ex.Message);
            LoggingService.LogException(ex, "CleaningVM.CleanAsync");
        }
        finally
        {
            IsCleaning = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (Application.Current?.Dispatcher?.CheckAccess() == true)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        else
        {
            Application.Current?.Dispatcher?.Invoke(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
        }
    }
}