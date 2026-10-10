using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Coclico.Converters;
using Coclico.Services;
using Microsoft.Win32;

namespace Coclico.Views;

public partial class ProgramsView : UserControl, INotifyPropertyChanged
{
    private readonly InstalledProgramsService _programsService = ServiceContainer.GetRequired<InstalledProgramsService>();
    private InstalledProgramsService.FilterGroup _selectedFilterGroup;
    private List<ProgramDisplayInfo> _allPrograms = [];
    private List<ProgramDisplayInfo> _games = [];
    private Action<string>? _genericInputCallback;
    private DispatcherTimer? _searchDebounce;

    public ObservableCollection<ProgramDisplayInfo> AllPrograms { get; } = [];
    public ObservableCollection<string> Categories { get; private set; }
    public ObservableCollection<InstalledProgramsService.FilterGroup> FilterGroups { get; private set; }
    public ObservableCollection<ProgramDisplayInfo> RecentPrograms { get; } = [];

    public ICommand AddGroupCommand { get; }
    public ICommand EditGroupCommand { get; }
    public ICommand DeleteGroupCommand { get; }
    public ICommand AddCategoryCommand { get; }
    public ICommand EditCategoryCommand { get; }
    public ICommand DeleteCategoryCommand { get; }
    public ICommand OpenConfigCommand { get; }
    public ICommand CloseConfigCommand { get; }
    public ICommand CloseManualAddCommand { get; }
    public ICommand ConfirmManualAddCommand { get; }
    public ICommand CloseGenericInputCommand { get; }
    public ICommand ConfirmGenericInputCommand { get; }

    public InstalledProgramsService.FilterGroup SelectedFilterGroup
    {
        get => _selectedFilterGroup;
        set
        {
            _selectedFilterGroup = value;
            OnNotifyPropertyChanged();
            UpdateUI();
        }
    }

    public string SelectedCategory
    {
        get;
        set { field = value; OnNotifyPropertyChanged(); UpdateUI(); }
    } = "Tout";

    public bool IsLoading
    {
        get;
        set { field = value; OnNotifyPropertyChanged(); }
    }

    public bool IsConfigOpen
    {
        get;
        set { field = value; OnNotifyPropertyChanged(); }
    }

    public bool IsGridMode
    {
        get;
        set
        {
            field = value;
            OnNotifyPropertyChanged();
            UpdateProgramsLayout();
        }
    }

    public bool IsManualAddOpen
    {
        get;
        set { field = value; OnNotifyPropertyChanged(); }
    }

    public string NewAppName
    {
        get;
        set { field = value; OnNotifyPropertyChanged(); }
    } = string.Empty;

    public string NewAppPath
    {
        get;
        set { field = value; OnNotifyPropertyChanged(); }
    } = string.Empty;

    public string NewAppCategory
    {
        get;
        set { field = value; OnNotifyPropertyChanged(); }
    } = "Logiciel";

    public bool IsGenericInputOpen
    {
        get;
        set { field = value; OnNotifyPropertyChanged(); }
    }

    public string GenericInputTitle
    {
        get;
        set { field = value; OnNotifyPropertyChanged(); }
    } = string.Empty;

    public string GenericInputLabel
    {
        get;
        set { field = value; OnNotifyPropertyChanged(); }
    } = string.Empty;

    public string GenericInputText
    {
        get;
        set { field = value; OnNotifyPropertyChanged(); }
    } = string.Empty;

    public string CurrentTab
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnNotifyPropertyChanged();
                OnNotifyPropertyChanged(nameof(IsTabAll));
                OnNotifyPropertyChanged(nameof(IsTabGames));
                OnNotifyPropertyChanged(nameof(IsTabSoftware));
                OnNotifyPropertyChanged(nameof(IsTabSystem));
                UpdateUI();
            }
        }
    } = "All";

    public bool IsTabAll => CurrentTab == "All";
    public bool IsTabGames => CurrentTab == "Games";
    public bool IsTabSoftware => CurrentTab == "Software";
    public bool IsTabSystem => CurrentTab == "System";

    public int CountAll => _allPrograms.Count;
    public int CountGames => _allPrograms.Count(p => p.IsGame);
    public int CountSoftware => _allPrograms.Count(p => !p.IsGame && !p.IsSystemTool);
    public int CountSystem => _allPrograms.Count(p => p.IsSystemTool);

    public ObservableCollection<string> SortOptions { get; } =
    [
        "Nom (A → Z)",
        "Nom (Z → A)",
        "Taille (Décroissante)",
        "Taille (Croissante)",
        "Éditeur (A → Z)"
    ];

    public string SelectedSort
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnNotifyPropertyChanged();
                UpdateUI();
            }
        }
    } = "Nom (A → Z)";

    public bool HasNoResults
    {
        get;
        set { field = value; OnNotifyPropertyChanged(); }
    }

    public string HeaderStatsText => _allPrograms.Count == 0
                ? "Chargement de la bibliothèque..."
                : $"{_allPrograms.Count} applications  •  {_games.Count} jeux  •  {TotalLibrarySizeText}";

    public string TotalLibrarySizeText
    {
        get
        {
            long total = _allPrograms.Sum(p => p.SizeBytes);
            return FormatSize(total);
        }
    }

    public bool IsSearchEmpty => string.IsNullOrEmpty(SearchBox?.Text);

    private static SolidColorBrush FreezeBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static readonly SolidColorBrush ActiveBtnBg = FreezeBrush(Color.FromRgb(79, 70, 229));
    private static readonly SolidColorBrush InactiveBtnBg = FreezeBrush(Colors.Transparent);
    private static readonly SolidColorBrush ActiveBtnFg = FreezeBrush(Colors.White);
    private static readonly SolidColorBrush InactiveBtnFg = FreezeBrush(Color.FromRgb(148, 163, 184));

    public SolidColorBrush GridBtnBg => IsGridMode ? ActiveBtnBg : InactiveBtnBg;
    public SolidColorBrush GridBtnFg => IsGridMode ? ActiveBtnFg : InactiveBtnFg;
    public SolidColorBrush ListBtnBg => !IsGridMode ? ActiveBtnBg : InactiveBtnBg;
    public SolidColorBrush ListBtnFg => !IsGridMode ? ActiveBtnFg : InactiveBtnFg;

    private void BtnSetGrid_Click(object sender, RoutedEventArgs e)
    {
        IsGridMode = true;
        OnNotifyPropertyChanged(nameof(GridBtnBg));
        OnNotifyPropertyChanged(nameof(GridBtnFg));
        OnNotifyPropertyChanged(nameof(ListBtnBg));
        OnNotifyPropertyChanged(nameof(ListBtnFg));
    }

    private void BtnSetList_Click(object sender, RoutedEventArgs e)
    {
        IsGridMode = false;
        OnNotifyPropertyChanged(nameof(GridBtnBg));
        OnNotifyPropertyChanged(nameof(GridBtnFg));
        OnNotifyPropertyChanged(nameof(ListBtnBg));
        OnNotifyPropertyChanged(nameof(ListBtnFg));
    }

    public int TotalAppsCount => _allPrograms.Count;
    public int TotalGamesCount => _games.Count;

    public string SourceStats
    {
        get
        {
            var all = _allPrograms.Concat(_games).ToList();
            return all.Count == 0
                ? "\u2014"
                : string.Join("\n", all
                .GroupBy(p => p.Source)
                .OrderByDescending(g => g.Count())
                .Take(6)
                .Select(g => $"{g.Key}: {g.Count()}"));
        }
    }

    public string CategoryStats => _allPrograms.Count == 0
                ? "\u2014"
                : string.Join("\n", _allPrograms
                .GroupBy(p => p.Category)
                .OrderByDescending(g => g.Count())
                .Take(6)
                .Select(g => $"{g.Key}: {g.Count()}"));

    private static string L(string key)
    {
        try
        {
            return Application.Current?.TryFindResource(key) as string ?? ServiceContainer.GetOptional<LocalizationService>()?.Get(key) ?? key;
        }
        catch { return key; }
    }

    public ProgramsView()
    {
        InitializeComponent();
        List<string> catList = _programsService.GetCategories();
        if (!catList.Contains("Tout"))
        {
            catList.Insert(0, "Tout");
        }

        Categories = new ObservableCollection<string>(catList);
        FilterGroups = new ObservableCollection<InstalledProgramsService.FilterGroup>(_programsService.GetFilterGroups());

        foreach (InstalledProgramsService.FilterGroup group in FilterGroups)
        {
            group.PropertyChanged += OnGroupChanged;
        }

        Unloaded += OnUnloaded;

        AddGroupCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => AddGroup());
        EditGroupCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(p => EditGroup(p as InstalledProgramsService.FilterGroup));
        DeleteGroupCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(p => DeleteGroup(p as InstalledProgramsService.FilterGroup));
        AddCategoryCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => AddCategory());
        EditCategoryCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(p => EditCategory(p as string));
        DeleteCategoryCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(p => DeleteCategory(p as string));
        OpenConfigCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => IsConfigOpen = true);
        CloseConfigCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => IsConfigOpen = false);
        CloseManualAddCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => IsManualAddOpen = false);
        ConfirmManualAddCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => ConfirmManualAdd());
        CloseGenericInputCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => IsGenericInputOpen = false);
        ConfirmGenericInputCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ =>
        {
            string trimmed = GenericInputText?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(trimmed))
            {
                _genericInputCallback?.Invoke(trimmed);
                IsGenericInputOpen = false;
            }
        });

        _selectedFilterGroup = FilterGroups.FirstOrDefault()
            ?? new InstalledProgramsService.FilterGroup { Name = "Bibliothèque", IsStatic = true };

        IsGridMode = true;
        DataContext = this;

        _ = LoadProgramsAsync();
    }

    private void OnGroupChanged(object? s, PropertyChangedEventArgs e)
    {
        UpdateUI();
        _programsService.SaveFilterGroups(FilterGroups.ToList());
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        foreach (InstalledProgramsService.FilterGroup group in FilterGroups ?? [])
        {
            group.PropertyChanged -= OnGroupChanged;
        }

        _searchDebounce?.Stop();
    }

    private void UpdateProgramsLayout()
    {
        UpdateUI();
    }

    private void BtnToggleView_Click(object sender, RoutedEventArgs e)
    {
        IsGridMode = !IsGridMode;
    }

    private void ShowGenericInput(string title, string label, string defaultValue, Action<string> callback)
    {
        GenericInputTitle = title;
        GenericInputLabel = label;
        GenericInputText = defaultValue;
        _genericInputCallback = callback;
        IsGenericInputOpen = true;
    }

    private void ConfirmManualAdd()
    {
        if (string.IsNullOrEmpty(NewAppPath) || !File.Exists(NewAppPath))
        {
            _ = MessageBox.Show(L("Programs_SelectValidExe"));
            return;
        }

        var newApp = new InstalledProgramsService.ProgramInfo
        {
            Name = string.IsNullOrEmpty(NewAppName) ? Path.GetFileNameWithoutExtension(NewAppPath) : NewAppName,
            ExePath = NewAppPath,
            InstallPath = Path.GetDirectoryName(NewAppPath) ?? string.Empty,
            Source = "Manuel",
            Category = NewAppCategory,
            IconPath = NewAppPath
        };

        _programsService.AddManualApplication(newApp);
        IsManualAddOpen = false;
        _ = LoadProgramsAsync(forceRefresh: false);
    }

    private ProgramDisplayInfo MapToDisplayInfo(InstalledProgramsService.ProgramInfo p)
    {
        bool isGame = p.Category.Equals("Jeux", StringComparison.OrdinalIgnoreCase) ||
                      p.Source is "Steam" or "Epic Games" or "GOG" or "Ubisoft" or "EA" or "Rockstar" ||
                      (!string.IsNullOrEmpty(p.InstallPath) && (p.InstallPath.Contains("games", StringComparison.OrdinalIgnoreCase) || p.InstallPath.Contains("jeux", StringComparison.OrdinalIgnoreCase))) ||
                      InstalledProgramsService.IsKnownGame(p.Name);

        bool isSystem = p.Category is "Outils Windows" or "Pilote" or "Composant" ||
                        p.Name.Contains("Visual C++", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Contains("DirectX", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Contains(".NET", StringComparison.OrdinalIgnoreCase) ||
                        (p.Publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) &&
                         (p.Name.Contains("Runtime", StringComparison.OrdinalIgnoreCase) ||
                          p.Name.Contains("Driver", StringComparison.OrdinalIgnoreCase) ||
                          p.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                          p.Name.Contains("Distributable", StringComparison.OrdinalIgnoreCase)));

        string category = p.Category;
        if (isGame && (string.IsNullOrEmpty(category) || category == "Logiciel"))
        {
            category = "Jeux";
        }
        else if (isSystem && (string.IsNullOrEmpty(category) || category == "Logiciel"))
        {
            category = "Outils Windows";
        }

        return new ProgramDisplayInfo
        {
            Name = p.Name,
            Publisher = p.Publisher,
            InstallPath = p.InstallPath,
            ExePath = p.ExePath,
            Version = p.Version,
            Source = p.Source,
            IconPath = p.IconPath,
            HasIcon = !string.IsNullOrEmpty(p.IconPath),
            Category = category,
            SizeBytes = p.SizeBytes,
            UninstallString = p.UninstallString,
            IsGame = isGame,
            IsSystemTool = isSystem,
            SizeText = FormatSize(p.SizeBytes)
        };
    }

    private async Task LoadProgramsAsync(bool forceRefresh = false)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;

        try
        {
            List<InstalledProgramsService.ProgramInfo> programs = await _programsService.GetAllInstalledProgramsAsync(forceRefresh: forceRefresh);

            _allPrograms = programs
                .Where(p => !string.IsNullOrEmpty(p.Name) && !p.Name.Contains("Coclico", StringComparison.OrdinalIgnoreCase))
                .Select(MapToDisplayInfo)
                .ToList();

            _games = _allPrograms.Where(p => p.IsGame).ToList();

            IEnumerable<string> iconPaths = _allPrograms
                .Where(p => p.HasIcon)
                .Select(p => p.IconPath)
                .Distinct();
            _ = FileIconConverter.PreloadAllAsync(iconPaths);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                AllPrograms.Clear();
                foreach (ProgramDisplayInfo p in _allPrograms)
                {
                    AllPrograms.Add(p);
                }

                UpdateUI();
            }, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "ProgramsView.LoadProgramsAsync");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateUI()
    {
        string searchText = SearchBox?.Text?.Trim() ?? string.Empty;

        bool MatchesSearch(ProgramDisplayInfo program)
        {
            return string.IsNullOrEmpty(searchText) ||
            (!string.IsNullOrEmpty(program.Name) && program.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(program.Publisher) && program.Publisher.Contains(searchText, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(program.Source) && program.Source.Contains(searchText, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(program.Category) && program.Category.Contains(searchText, StringComparison.OrdinalIgnoreCase));
        }

        IEnumerable<ProgramDisplayInfo> query = CurrentTab switch
        {
            "Games" => _allPrograms.Where(p => p.IsGame),
            "Software" => _allPrograms.Where(p => !p.IsGame && !p.IsSystemTool),
            "System" => _allPrograms.Where(p => p.IsSystemTool),
            _ => _allPrograms
        };

        if (!string.IsNullOrEmpty(SelectedCategory) && SelectedCategory != "Tout")
        {
            query = query.Where(p => string.Equals(p.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(searchText))
        {
            query = query.Where(MatchesSearch);
        }

        query = SelectedSort switch
        {
            "Nom (Z → A)" => query.OrderByDescending(p => p.Name, StringComparer.OrdinalIgnoreCase),
            "Taille (Décroissante)" => query.OrderByDescending(p => p.SizeBytes).ThenBy(p => p.Name),
            "Taille (Croissante)" => query.OrderBy(p => p.SizeBytes > 0 ? p.SizeBytes : long.MaxValue).ThenBy(p => p.Name),
            "Éditeur (A → Z)" => query.OrderBy(p => p.Publisher, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Name),
            _ => query.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
        };

        var filtered = query.ToList();

        if (filtered.Count == 0 && !string.IsNullOrEmpty(searchText))
        {
            filtered = _allPrograms.Where(MatchesSearch).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        ProgramsList?.ItemsSource = filtered;
        ProgramsGrid?.ItemsSource = filtered;

        HasNoResults = !IsLoading && filtered.Count == 0;

        OnNotifyPropertyChanged(nameof(CountAll));
        OnNotifyPropertyChanged(nameof(CountGames));
        OnNotifyPropertyChanged(nameof(CountSoftware));
        OnNotifyPropertyChanged(nameof(CountSystem));
        OnNotifyPropertyChanged(nameof(TotalAppsCount));
        OnNotifyPropertyChanged(nameof(TotalGamesCount));
        OnNotifyPropertyChanged(nameof(SourceStats));
        OnNotifyPropertyChanged(nameof(CategoryStats));
        OnNotifyPropertyChanged(nameof(HeaderStatsText));
        OnNotifyPropertyChanged(nameof(TotalLibrarySizeText));
    }

    public ProgramDisplayInfo? SelectedProgram
    {
        get;
        set
        {
            field = value;
            IsEditing = false;
            OnNotifyPropertyChanged();
        }
    }

    public bool IsEditing
    {
        get;
        set { field = value; OnNotifyPropertyChanged(); }
    }

    public bool IsDetailsOpen
    {
        get;
        set { field = value; OnNotifyPropertyChanged(); }
    }

    private void ProgramItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Border border &&
            border.DataContext is ProgramDisplayInfo app &&
            e is MouseButtonEventArgs mArgs)
        {
            SelectedProgram = app;
            IsDetailsOpen = true;
            if (mArgs.ClickCount >= 2)
            {
                LaunchAppAndForget(app);
            }
        }
    }

    private async void LaunchAppAndForget(ProgramDisplayInfo app)
    {
        try { await LaunchApp(app); }
        catch (Exception ex) { LoggingService.LogException(ex, "ProgramsView.LaunchAppAndForget"); }
    }

    private async Task LaunchApp(ProgramDisplayInfo app)
    {
        bool exists = await Task.Run(() =>
            !string.IsNullOrEmpty(app.ExePath) && File.Exists(app.ExePath));

        if (!exists)
        {
            _ = MessageBox.Show(L("Programs_ExeNotFound"), L("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        app.IsLaunching = true;
        try
        {
            string workDir = await Task.Run(() =>
                !string.IsNullOrEmpty(app.InstallPath) && Directory.Exists(app.InstallPath)
                    ? app.InstallPath
                    : Path.GetDirectoryName(app.ExePath) ?? string.Empty);

            _ = await Task.Run(() => Process.Start(
                new ProcessStartInfo
                {
                    FileName = app.ExePath,
                    WorkingDirectory = workDir,
                    UseShellExecute = true
                }));

            ProgramDisplayInfo? existing = RecentPrograms.FirstOrDefault(p => p.ExePath == app.ExePath);
            if (existing != null)
            {
                _ = RecentPrograms.Remove(existing);
            }

            RecentPrograms.Insert(0, app);
            if (RecentPrograms.Count > 5)
            {
                RecentPrograms.RemoveAt(5);
            }

        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "ProgramsView.LaunchApp");
            _ = MessageBox.Show(string.Format(L("Programs_LaunchError"), ex.Message));
        }
        finally
        {
            app.IsLaunching = false;
        }
    }

    private void BtnLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProgram != null)
        {
            LaunchAppAndForget(SelectedProgram);
            IsDetailsOpen = false;
        }
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProgram != null && !string.IsNullOrEmpty(SelectedProgram.InstallPath))
        {
            try { _ = Process.Start("explorer.exe", SelectedProgram.InstallPath); }
            catch (Exception ex) { LoggingService.LogException(ex, "ProgramsView.BtnOpenFolder"); }
        }
    }

    private void BtnCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProgram == null)
        {
            return;
        }

        string path = !string.IsNullOrEmpty(SelectedProgram.ExePath) ? SelectedProgram.ExePath : SelectedProgram.InstallPath;
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                Clipboard.SetText(path);
                _ = MessageBox.Show(string.Format(L("Programs_PathCopied"), path), "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "ProgramsView.BtnCopyPath_Click");
            }
        }
    }

    private void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProgram == null)
        {
            return;
        }

        ProgramDisplayInfo app = SelectedProgram;

        MessageBoxResult confirm = MessageBox.Show(
            string.Format(L("Programs_ConfirmUninstall"), app.Name),
            L("Programs_ConfirmUninstallTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(app.UninstallString))
            {
                _ = Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
                return;
            }

            ParsedUninstallCommand? parsed = UninstallCommandParser.Parse(app.UninstallString);
            if (parsed == null)
            {
                _ = MessageBox.Show("Commande de désinstallation invalide ou introuvable.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!IsUninstallCommandAllowed(parsed))
            {
                LoggingService.LogWarning(
                    $"[ProgramsView] Désinstallation bloquée par la politique de sécurité : '{app.Name}' -> {app.UninstallString}");
                _ = MessageBox.Show(
                    "La commande de désinstallation a été bloquée par la politique de sécurité de Coclico.",
                    L("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (parsed.IsUri)
            {
                _ = Process.Start(new ProcessStartInfo(parsed.ExecutableOrUri) { UseShellExecute = true });
                return;
            }

            if (parsed.IsMsi)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = parsed.ExecutableOrUri,
                    Arguments = parsed.Arguments,
                    UseShellExecute = true
                };
                _ = Process.Start(psi);
                return;
            }

            string expandedExe = Environment.ExpandEnvironmentVariables(parsed.ExecutableOrUri);
            if (File.Exists(expandedExe))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = expandedExe,
                    Arguments = parsed.Arguments,
                    WorkingDirectory = Path.GetDirectoryName(expandedExe) ?? string.Empty,
                    UseShellExecute = true
                };
                _ = Process.Start(psi);
                return;
            }

            _ = MessageBox.Show(
                $"Le programme de désinstallation est introuvable : {parsed.ExecutableOrUri}",
                "Information", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "ProgramsView.BtnUninstall_Click");
            _ = MessageBox.Show(string.Format(L("Programs_UninstallError"), ex.Message), L("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool IsUninstallCommandAllowed(ParsedUninstallCommand parsed)
    {
        ISecurityPolicy? policy = ServiceContainer.GetOptional<ISecurityPolicy>();
        if (policy == null)
        {
            return true;
        }

        if (policy.IsCommandBlocked(parsed.ExecutableOrUri.ToLowerInvariant()))
        {
            return false;
        }

        string full = string.IsNullOrWhiteSpace(parsed.Arguments)
            ? parsed.ExecutableOrUri
            : $"{parsed.ExecutableOrUri} {parsed.Arguments}";
        if (policy.IsCommandBlocked(full.ToLowerInvariant()))
        {
            return false;
        }

        return true;
    }

    private void BtnSaveName_Click(object sender, RoutedEventArgs e)
    {
        if (!IsEditing)
        {
            IsEditing = true;
            _ = TxtProgramName.Focus();
            TxtProgramName.SelectAll();
        }
        else
        {
            if (SelectedProgram != null)
            {
                _programsService.SaveCustomAppData(
                    SelectedProgram.ExePath,
                    SelectedProgram.InstallPath,
                    SelectedProgram.Name,
                    SelectedProgram.Name,
                    SelectedProgram.Category);
                UpdateUI();
            }
            IsEditing = false;
        }
    }

    private void BtnCloseDetails_Click(object sender, RoutedEventArgs e)
    {
        SelectedProgram = null;
        IsDetailsOpen = false;
        IsEditing = false;
    }

    private void BtnAssignShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProgram == null)
        {
            return;
        }

        ShowGenericInput(
            "RACCOURCI CLAVIER",
            L("Programs_ShortcutPrompt"),
            SelectedProgram.Shortcut,
            input =>
            {
                SelectedProgram.Shortcut = input.Trim();
                _programsService.SaveCustomAppData(
                    SelectedProgram.ExePath,
                    SelectedProgram.InstallPath,
                    SelectedProgram.Name,
                    SelectedProgram.Name,
                    SelectedProgram.Category);
                OnNotifyPropertyChanged(nameof(SelectedProgram));
            });
    }

    private void BtnOpenConfig_Click(object sender, RoutedEventArgs e)
    {
        IsConfigOpen = true;
    }

    private void BtnCloseConfig_Click(object sender, RoutedEventArgs e)
    {
        IsConfigOpen = false;
    }

    private void CloseConfig_Click(object sender, RoutedEventArgs e)
    {
        IsConfigOpen = false;
    }

    private void FilterGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb &&
            rb.DataContext is InstalledProgramsService.FilterGroup group)
        {
            SelectedFilterGroup = group;
            if (!string.IsNullOrEmpty(group.CategoryFilter))
            {
                SelectedCategory = group.CategoryFilter;
            }
        }
    }

    private void TabAll_Click(object sender, RoutedEventArgs e)
    {
        CurrentTab = "All";
    }

    private void TabGames_Click(object sender, RoutedEventArgs e)
    {
        CurrentTab = "Games";
    }

    private void TabSoftware_Click(object sender, RoutedEventArgs e)
    {
        CurrentTab = "Software";
    }

    private void TabSystem_Click(object sender, RoutedEventArgs e)
    {
        CurrentTab = "System";
    }

    private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox?.Text = string.Empty;
        OnNotifyPropertyChanged(nameof(IsSearchEmpty));
        UpdateUI();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        OnNotifyPropertyChanged(nameof(IsSearchEmpty));
        if (_searchDebounce == null)
        {
            _searchDebounce = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); UpdateUI(); };
        }
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void AddGroup()
    {
        if (FilterGroups.Count >= 6)
        {
            _ = MessageBox.Show("Maximum 5 groupes personnalis\u00e9s autoris\u00e9s.");
            return;
        }

        ShowGenericInput("NOUVEAU GROUPE", "Nom du nouveau groupe :", "Mon Groupe", input =>
        {
            if (FilterGroups.Any(g => g.Name.Equals(input, StringComparison.OrdinalIgnoreCase)))
            {
                _ = MessageBox.Show(L("Programs_GroupNameExists"));
                return;
            }
            var newGroup = new InstalledProgramsService.FilterGroup { Name = input, IsStatic = false };
            newGroup.PropertyChanged += OnGroupChanged;
            FilterGroups.Add(newGroup);
            _programsService.SaveFilterGroups(FilterGroups.ToList());
            UpdateUI();
        });
    }

    private void EditGroup(InstalledProgramsService.FilterGroup? group)
    {
        if (group == null || group.IsStatic)
        {
            return;
        }

        ShowGenericInput("MODIFIER GROUPE", "Nouveau nom :", group.Name, input =>
        {
            group.Name = input;
            _programsService.SaveFilterGroups(FilterGroups.ToList());
            UpdateUI();
        });
    }

    private void DeleteGroup(InstalledProgramsService.FilterGroup? group)
    {
        if (group == null || group.IsStatic)
        {
            return;
        }

        group.PropertyChanged -= OnGroupChanged;
        _ = FilterGroups.Remove(group);
        if (SelectedFilterGroup == group)
        {
            SelectedFilterGroup = FilterGroups.FirstOrDefault()
                ?? new InstalledProgramsService.FilterGroup { Name = "Biblioth\u00e8que", IsStatic = true };
        }

        _programsService.SaveFilterGroups(FilterGroups.ToList());
        UpdateUI();
    }

    private void AddCategory()
    {
        if (Categories.Count >= 256)
        {
            _ = MessageBox.Show("Maximum 255 cat\u00e9gories autoris\u00e9es.");
            return;
        }

        ShowGenericInput("NOUVELLE CAT\u00c9GORIE", "Nom de la cat\u00e9gorie :", "Autre", input =>
        {
            if (Categories.Any(c => c.Equals(input, StringComparison.OrdinalIgnoreCase)))
            {
                _ = MessageBox.Show(L("Programs_CategoryExists"));
                return;
            }
            Categories.Add(input);
            _programsService.SaveCategories(Categories.ToList());
            UpdateUI();
        });
    }

    private void EditCategory(string? category)
    {
        if (string.IsNullOrEmpty(category) || category == "Tout")
        {
            return;
        }

        ShowGenericInput("MODIFIER CAT\u00c9GORIE", "Nouveau nom :", category, input =>
        {
            int index = Categories.IndexOf(category);
            if (index != -1)
            {
                string oldName = Categories[index];
                Categories[index] = input;
                if (SelectedCategory == category)
                {
                    SelectedCategory = input;
                }

                foreach (ProgramDisplayInfo p in _allPrograms.Where(x => x.Category == oldName))
                {
                    p.Category = input;
                    _programsService.SaveCustomAppData(p.ExePath, p.InstallPath, p.Name, null, input);
                }
                foreach (ProgramDisplayInfo p in _games.Where(x => x.Category == oldName))
                {
                    p.Category = input;
                    _programsService.SaveCustomAppData(p.ExePath, p.InstallPath, p.Name, null, input);
                }

                _programsService.SaveCategories(Categories.ToList());
            }
            UpdateUI();
        });
    }

    private void DeleteCategory(string? category)
    {
        if (string.IsNullOrEmpty(category) || category == "Tout")
        {
            return;
        }

        foreach (ProgramDisplayInfo p in _allPrograms.Where(x => x.Category == category))
        {
            p.Category = "Logiciel";
            _programsService.SaveCustomAppData(p.ExePath, p.InstallPath, p.Name, null, "Logiciel");
        }
        foreach (ProgramDisplayInfo p in _games.Where(x => x.Category == category))
        {
            p.Category = "Logiciel";
            _programsService.SaveCustomAppData(p.ExePath, p.InstallPath, p.Name, null, "Logiciel");
        }

        _ = Categories.Remove(category);
        if (SelectedCategory == category)
        {
            SelectedCategory = "Tout";
        }

        _programsService.SaveCategories(Categories.ToList());
        UpdateUI();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadProgramsAsync(forceRefresh: true);
    }

    private void BtnAddManual_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "S\u00e9lectionner une application"
        };

        if (dialog.ShowDialog() == true)
        {
            NewAppPath = dialog.FileName;
            NewAppName = Path.GetFileNameWithoutExtension(dialog.FileName);
            NewAppCategory = Categories.Count > 2 ? Categories[2] : (Categories.Count > 0 ? Categories[0] : "Logiciel");
            IsManualAddOpen = true;
        }
    }

    private static string FormatSize(long bytes)
    {
        return bytes <= 0
            ? string.Empty
            : bytes < 1024 * 1024
            ? $"{bytes / 1024:F0} KB"
            : bytes < 1024L * 1024 * 1024 ? $"{bytes / (1024.0 * 1024):F0} MB" : $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnNotifyPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class ProgramDisplayInfo : INotifyPropertyChanged
    {
        public string Name
        {
            get;
            set { field = value; OnPropertyChanged(); }
        } = string.Empty;

        public string Publisher { get; set; } = string.Empty;
        public string InstallPath { get; set; } = string.Empty;
        public string ExePath { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string SizeText { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string UninstallString { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;
        public bool HasIcon { get; set; }
        public bool IsGame { get; set; }
        public bool IsSystemTool { get; set; }

        public bool CanUninstall => !string.IsNullOrWhiteSpace(UninstallString) || Source == "Microsoft Store";

        public string Category
        {
            get;
            set { field = value; OnPropertyChanged(); }
        } = string.Empty;

        public bool IsLaunching
        {
            get;
            set { field = value; OnPropertyChanged(); }
        }

        public string Shortcut
        {
            get;
            set { field = value; OnPropertyChanged(); }
        } = string.Empty;

        public string VersionDisplay => string.IsNullOrWhiteSpace(Version) ? "—" : Version;
        public string SizeDisplay => string.IsNullOrWhiteSpace(SizeText) ? "—" : SizeText;

        private static readonly SolidColorBrush GameBg = FreezeBrush(Color.FromArgb(50, 16, 185, 129));
        private static readonly SolidColorBrush GameFg = FreezeBrush(Color.FromRgb(110, 231, 183));
        private static readonly SolidColorBrush SystemBg = FreezeBrush(Color.FromArgb(55, 245, 158, 11));
        private static readonly SolidColorBrush SystemFg = FreezeBrush(Color.FromRgb(253, 230, 138));
        private static readonly SolidColorBrush SoftwareBg = FreezeBrush(Color.FromArgb(50, 99, 102, 241));
        private static readonly SolidColorBrush SoftwareFg = FreezeBrush(Color.FromRgb(199, 210, 254));

        public SolidColorBrush CategoryBgBrush => IsGame ? GameBg : (IsSystemTool ? SystemBg : SoftwareBg);
        public SolidColorBrush CategoryFgBrush => IsGame ? GameFg : (IsSystemTool ? SystemFg : SoftwareFg);
        public string CategoryDisplayName => IsGame ? "JEU" : (IsSystemTool ? "SYSTÈME" : "LOGICIEL");

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}