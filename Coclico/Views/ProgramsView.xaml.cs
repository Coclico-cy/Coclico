using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Coclico.Converters;
using Coclico.Services;

namespace Coclico.Views
{
    public partial class ProgramsView : System.Windows.Controls.UserControl, INotifyPropertyChanged
    {
        private readonly InstalledProgramsService _programsService = InstalledProgramsService.Instance;

        private bool _isLoading;
        private bool _isConfigOpen;
        private InstalledProgramsService.FilterGroup _selectedFilterGroup;
        private bool _isEditing;
        private string _selectedCategory = "Tout";
        private List<ProgramDisplayInfo> _allPrograms = new();
        private List<ProgramDisplayInfo> _games = new();

        private bool _isManualAddOpen;
        private string _newAppName = string.Empty;
        private string _newAppPath = string.Empty;
        private string _newAppCategory = "Logiciel";

        private bool _isGenericInputOpen;
        private string _genericInputTitle = string.Empty;
        private string _genericInputLabel = string.Empty;
        private string _genericInputText = string.Empty;
        private System.Action<string>? _genericInputCallback;

        public static ObservableCollection<ProgramDisplayInfo> AllPrograms { get; } = new();
        public ObservableCollection<string> Categories { get; private set; }
        public ObservableCollection<InstalledProgramsService.FilterGroup> FilterGroups { get; private set; }
        public ObservableCollection<ProgramDisplayInfo> RecentPrograms { get; } = new();

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
            get => _selectedCategory;
            set { _selectedCategory = value; OnNotifyPropertyChanged(); UpdateUI(); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnNotifyPropertyChanged(); }
        }

        public bool IsConfigOpen
        {
            get => _isConfigOpen;
            set { _isConfigOpen = value; OnNotifyPropertyChanged(); }
        }

        private bool _isGridMode;
        public bool IsGridMode
        {
            get => _isGridMode;
            set
            {
                _isGridMode = value;
                OnNotifyPropertyChanged();
                UpdateProgramsLayout();
            }
        }

        public bool IsManualAddOpen
        {
            get => _isManualAddOpen;
            set { _isManualAddOpen = value; OnNotifyPropertyChanged(); }
        }

        public string NewAppName
        {
            get => _newAppName;
            set { _newAppName = value; OnNotifyPropertyChanged(); }
        }

        public string NewAppPath
        {
            get => _newAppPath;
            set { _newAppPath = value; OnNotifyPropertyChanged(); }
        }

        public string NewAppCategory
        {
            get => _newAppCategory;
            set { _newAppCategory = value; OnNotifyPropertyChanged(); }
        }

        public bool IsGenericInputOpen
        {
            get => _isGenericInputOpen;
            set { _isGenericInputOpen = value; OnNotifyPropertyChanged(); }
        }

        public string GenericInputTitle
        {
            get => _genericInputTitle;
            set { _genericInputTitle = value; OnNotifyPropertyChanged(); }
        }

        public string GenericInputLabel
        {
            get => _genericInputLabel;
            set { _genericInputLabel = value; OnNotifyPropertyChanged(); }
        }

        public string GenericInputText
        {
            get => _genericInputText;
            set { _genericInputText = value; OnNotifyPropertyChanged(); }
        }

        public int TotalAppsCount => _allPrograms.Count;
        public int TotalGamesCount => _games.Count;

        public string SourceStats
        {
            get
            {
                var all = _allPrograms.Concat(_games).ToList();
                if (all.Count == 0) return "—";
                return string.Join("\n", all
                    .GroupBy(p => p.Source)
                    .OrderByDescending(g => g.Count())
                    .Take(6)
                    .Select(g => $"{g.Key}: {g.Count()}"));
            }
        }

        public string CategoryStats
        {
            get
            {
                if (_allPrograms.Count == 0) return "—";
                return string.Join("\n", _allPrograms
                    .GroupBy(p => p.Category)
                    .OrderByDescending(g => g.Count())
                    .Take(6)
                    .Select(g => $"{g.Key}: {g.Count()}"));
            }
        }

        public ProgramsView()
        {
            InitializeComponent();
            Categories   = new ObservableCollection<string>(_programsService.GetCategories());
            FilterGroups = new ObservableCollection<InstalledProgramsService.FilterGroup>(_programsService.GetFilterGroups());

            foreach (var group in FilterGroups)
                group.PropertyChanged += OnGroupChanged;

            AddGroupCommand            = new RelayCommand(_ => AddGroup());
            EditGroupCommand           = new RelayCommand(p => EditGroup(p as InstalledProgramsService.FilterGroup));
            DeleteGroupCommand         = new RelayCommand(p => DeleteGroup(p as InstalledProgramsService.FilterGroup));
            AddCategoryCommand         = new RelayCommand(_ => AddCategory());
            EditCategoryCommand        = new RelayCommand(p => EditCategory(p as string));
            DeleteCategoryCommand      = new RelayCommand(p => DeleteCategory(p as string));
            OpenConfigCommand          = new RelayCommand(_ => IsConfigOpen = true);
            CloseConfigCommand         = new RelayCommand(_ => IsConfigOpen = false);
            CloseManualAddCommand      = new RelayCommand(_ => IsManualAddOpen = false);
            ConfirmManualAddCommand    = new RelayCommand(_ => ConfirmManualAdd());
            CloseGenericInputCommand   = new RelayCommand(_ => IsGenericInputOpen = false);
            ConfirmGenericInputCommand = new RelayCommand(_ =>
            {
                var trimmed = GenericInputText?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(trimmed))
                {
                    _genericInputCallback?.Invoke(trimmed);
                    IsGenericInputOpen = false;
                }
            });

            _selectedFilterGroup = FilterGroups.FirstOrDefault()
                ?? new InstalledProgramsService.FilterGroup { Name = "Bibliothèque", IsStatic = true };

            IsGridMode = false;
            DataContext = this;

            _ = LoadProgramsAsync();
        }

        private void OnGroupChanged(object? s, PropertyChangedEventArgs e)
        {
            UpdateUI();
            _programsService.SaveFilterGroups(FilterGroups.ToList());
        }

        private void UpdateProgramsLayout()
        {
            UpdateUI();
        }

        private void BtnToggleView_Click(object sender, RoutedEventArgs e)
        {
            IsGridMode = !IsGridMode;
        }

        private void ShowGenericInput(string title, string label, string defaultValue, System.Action<string> callback)
        {
            GenericInputTitle     = title;
            GenericInputLabel     = label;
            GenericInputText      = defaultValue;
            _genericInputCallback = callback;
            IsGenericInputOpen    = true;
        }

        private void ConfirmManualAdd()
        {
            if (string.IsNullOrEmpty(NewAppPath) || !System.IO.File.Exists(NewAppPath))
            {
                MessageBox.Show("Veuillez sélectionner un exécutable valide.");
                return;
            }

            var newApp = new InstalledProgramsService.ProgramInfo
            {
                Name        = string.IsNullOrEmpty(NewAppName) ? System.IO.Path.GetFileNameWithoutExtension(NewAppPath) : NewAppName,
                ExePath     = NewAppPath,
                InstallPath = System.IO.Path.GetDirectoryName(NewAppPath) ?? string.Empty,
                Source      = "Manuel",
                Category    = NewAppCategory,
                IconPath    = NewAppPath
            };

            _programsService.AddManualApplication(newApp);
            IsManualAddOpen = false;
            _ = LoadProgramsAsync(forceRefresh: false);
        }

        private async Task LoadProgramsAsync(bool forceRefresh = false)
        {
            if (IsLoading) return;
            IsLoading = true;

            try
            {
                var programs = await _programsService.GetAllInstalledProgramsAsync(forceRefresh: forceRefresh);

                _allPrograms = programs.Select(p => new ProgramDisplayInfo
                {
                    Name        = p.Name,
                    Publisher   = p.Publisher,
                    InstallPath = p.InstallPath,
                    ExePath     = p.ExePath,
                    Version     = p.Version,
                    Source      = p.Source,
                    IconPath    = p.IconPath,
                    HasIcon     = !string.IsNullOrEmpty(p.IconPath),
                    Category    = p.Category,
                    SizeText    = FormatSize(p.SizeBytes)
                }).ToList();

                var gamesData = InstalledProgramsService.DetectGames(programs);
                _games = gamesData.Select(p => new ProgramDisplayInfo
                {
                    Name        = p.Name,
                    Publisher   = p.Publisher,
                    InstallPath = p.InstallPath,
                    ExePath     = p.ExePath,
                    Version     = p.Version,
                    Source      = p.Source,
                    IconPath    = p.IconPath,
                    HasIcon     = !string.IsNullOrEmpty(p.IconPath),
                    Category    = p.Category,
                    SizeText    = FormatSize(p.SizeBytes)
                }).ToList();

                var iconPaths = _allPrograms.Concat(_games)
                    .Where(p => p.HasIcon)
                    .Select(p => p.IconPath)
                    .Distinct();
                _ = FileIconToImageSourceConverter.PreloadAllAsync(iconPaths);

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AllPrograms.Clear();
                    foreach (var p in _allPrograms) AllPrograms.Add(p);
                    UpdateUI();
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (System.Exception ex)
            {
                Coclico.Services.LoggingService.LogException(ex, "ProgramsView.LoadProgramsAsync");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void UpdateUI()
        {
            var source     = (_selectedFilterGroup?.ShowsGames ?? false) ? _games : _allPrograms;
            var searchText = SearchBox?.Text ?? string.Empty;

            var filtered = source.Where(p =>
                (string.IsNullOrEmpty(searchText) ||
                 (!string.IsNullOrEmpty(p.Name) && p.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)) ||
                 (!string.IsNullOrEmpty(p.Publisher) && p.Publisher.Contains(searchText, StringComparison.OrdinalIgnoreCase)) ||
                 (!string.IsNullOrEmpty(p.Source) && p.Source.Contains(searchText, StringComparison.OrdinalIgnoreCase))) &&
                (string.IsNullOrEmpty(_selectedCategory) || _selectedCategory == "Tout" || p.Category == _selectedCategory)
            ).ToList();

            if (_selectedFilterGroup != null &&
                !string.IsNullOrEmpty(_selectedFilterGroup.CategoryFilter) &&
                _selectedFilterGroup.CategoryFilter != "Tout")
            {
                filtered = filtered.Where(p => p.Category == _selectedFilterGroup.CategoryFilter).ToList();
            }

            ProgramsList.ItemsSource = filtered;
            ProgramsGrid.ItemsSource = filtered;
            TxtCount.Text = (_selectedFilterGroup?.ShowsGames ?? false)
                ? $"{filtered.Count} titres correspondants"
                : $"{filtered.Count} logiciels au total";

            OnNotifyPropertyChanged(nameof(TotalAppsCount));
            OnNotifyPropertyChanged(nameof(TotalGamesCount));
            OnNotifyPropertyChanged(nameof(SourceStats));
            OnNotifyPropertyChanged(nameof(CategoryStats));
        }

        private ProgramDisplayInfo? _selectedProgram;
        public ProgramDisplayInfo? SelectedProgram
        {
            get => _selectedProgram;
            set
            {
                _selectedProgram = value;
                IsEditing = false;
                OnNotifyPropertyChanged();
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set { _isEditing = value; OnNotifyPropertyChanged(); }
        }

        private bool _isDetailsOpen;
        public bool IsDetailsOpen
        {
            get => _isDetailsOpen;
            set { _isDetailsOpen = value; OnNotifyPropertyChanged(); }
        }

        private void ProgramItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Border border &&
                border.DataContext is ProgramDisplayInfo app &&
                e is System.Windows.Input.MouseButtonEventArgs mArgs)
            {
                if (mArgs.ClickCount >= 2)
                    LaunchAppAndForget(app);
                else
                {
                    SelectedProgram = app;
                    IsDetailsOpen   = true;
                }
            }
        }

        private async void LaunchAppAndForget(ProgramDisplayInfo app) => await LaunchApp(app);

        private async Task LaunchApp(ProgramDisplayInfo app)
        {
            bool exists = await Task.Run(() =>
                !string.IsNullOrEmpty(app.ExePath) && System.IO.File.Exists(app.ExePath));

            if (!exists)
            {
                MessageBox.Show("Impossible de trouver l'exécutable.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            app.IsLaunching = true;
            try
            {
                string workDir = await Task.Run(() =>
                    !string.IsNullOrEmpty(app.InstallPath) && System.IO.Directory.Exists(app.InstallPath)
                        ? app.InstallPath
                        : System.IO.Path.GetDirectoryName(app.ExePath) ?? string.Empty);

                await Task.Run(() => System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName         = app.ExePath,
                        WorkingDirectory = workDir,
                        UseShellExecute  = true
                    }));

                var existing = RecentPrograms.FirstOrDefault(p => p.ExePath == app.ExePath);
                if (existing != null) RecentPrograms.Remove(existing);
                RecentPrograms.Insert(0, app);
                if (RecentPrograms.Count > 5) RecentPrograms.RemoveAt(5);

                await Task.Delay(500);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Erreur lors du lancement : {ex.Message}");
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
                try { System.Diagnostics.Process.Start("explorer.exe", SelectedProgram.InstallPath); }
                catch (System.Exception ex) { Coclico.Services.LoggingService.LogException(ex, "ProgramsView.BtnOpenFolder"); }
            }
        }

        private void BtnSaveName_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEditing)
            {
                IsEditing = true;
                TxtProgramName.Focus();
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
            IsDetailsOpen = false;
            IsEditing     = false;
        }

        private void BtnOpenConfig_Click(object sender, RoutedEventArgs e) => IsConfigOpen = true;
        private void BtnCloseConfig_Click(object sender, RoutedEventArgs e) => IsConfigOpen = false;
        private void CloseConfig_Click(object sender, RoutedEventArgs e) => IsConfigOpen = false;

        private void FilterGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton rb &&
                rb.DataContext is InstalledProgramsService.FilterGroup group)
            {
                SelectedFilterGroup = group;
                if (!string.IsNullOrEmpty(group.CategoryFilter))
                    SelectedCategory = group.CategoryFilter;
            }
        }

        private System.Windows.Threading.DispatcherTimer? _searchDebounce;

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_searchDebounce == null)
            {
                _searchDebounce = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = System.TimeSpan.FromMilliseconds(150)
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
                MessageBox.Show("Maximum 5 groupes personnalisés autorisés.");
                return;
            }

            ShowGenericInput("NOUVEAU GROUPE", "Nom du nouveau groupe :", "Mon Groupe", input =>
            {
                if (FilterGroups.Any(g => g.Name.Equals(input, System.StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("Un groupe avec ce nom existe déjà.");
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
            if (group == null || group.IsStatic) return;

            ShowGenericInput("MODIFIER GROUPE", "Nouveau nom :", group.Name, input =>
            {
                group.Name = input;
                _programsService.SaveFilterGroups(FilterGroups.ToList());
                UpdateUI();
            });
        }

        private void DeleteGroup(InstalledProgramsService.FilterGroup? group)
        {
            if (group == null || group.IsStatic) return;

            group.PropertyChanged -= OnGroupChanged;
            FilterGroups.Remove(group);
            if (SelectedFilterGroup == group)
                SelectedFilterGroup = FilterGroups.FirstOrDefault()
                    ?? new InstalledProgramsService.FilterGroup { Name = "Bibliothèque", IsStatic = true };
            _programsService.SaveFilterGroups(FilterGroups.ToList());
            UpdateUI();
        }

        private void AddCategory()
        {
            if (Categories.Count >= 256)
            {
                MessageBox.Show("Maximum 255 catégories autorisées.");
                return;
            }

            ShowGenericInput("NOUVELLE CATÉGORIE", "Nom de la catégorie :", "Autre", input =>
            {
                if (Categories.Any(c => c.Equals(input, System.StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("Cette catégorie existe déjà.");
                    return;
                }
                Categories.Add(input);
                _programsService.SaveCategories(Categories.ToList());
                UpdateUI();
            });
        }

        private void EditCategory(string? category)
        {
            if (string.IsNullOrEmpty(category) || category == "Tout") return;

            ShowGenericInput("MODIFIER CATÉGORIE", "Nouveau nom :", category, input =>
            {
                int index = Categories.IndexOf(category);
                if (index != -1)
                {
                    string oldName = Categories[index];
                    Categories[index] = input;
                    if (SelectedCategory == category) SelectedCategory = input;

                    foreach (var p in _allPrograms.Where(x => x.Category == oldName))
                    {
                        p.Category = input;
                        _programsService.SaveCustomAppData(p.ExePath, p.InstallPath, p.Name, null, input);
                    }
                    foreach (var p in _games.Where(x => x.Category == oldName))
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
            if (string.IsNullOrEmpty(category) || category == "Tout") return;

            foreach (var p in _allPrograms.Where(x => x.Category == category))
            {
                p.Category = "Logiciel";
                _programsService.SaveCustomAppData(p.ExePath, p.InstallPath, p.Name, null, "Logiciel");
            }
            foreach (var p in _games.Where(x => x.Category == category))
            {
                p.Category = "Logiciel";
                _programsService.SaveCustomAppData(p.ExePath, p.InstallPath, p.Name, null, "Logiciel");
            }

            Categories.Remove(category);
            if (SelectedCategory == category) SelectedCategory = "Tout";
            _programsService.SaveCategories(Categories.ToList());
            UpdateUI();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => _ = LoadProgramsAsync(forceRefresh: true);

        private void BtnAddManual_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
                Title  = "Sélectionner une application"
            };

            if (dialog.ShowDialog() == true)
            {
                NewAppPath      = dialog.FileName;
                NewAppName      = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                NewAppCategory  = Categories.Count > 2 ? Categories[2] : (Categories.Count > 0 ? Categories[0] : "Logiciel");
                IsManualAddOpen = true;
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0)                     return "—";
            if (bytes < 1024 * 1024)            return $"{bytes / 1024:F0} KB";
            if (bytes < 1024L * 1024 * 1024)    return $"{bytes / (1024.0 * 1024):F0} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnNotifyPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public class ProgramDisplayInfo : INotifyPropertyChanged
        {
            private string _name = string.Empty;
            public string Name
            {
                get => _name;
                set { _name = value; OnPropertyChanged(); }
            }

            public string Publisher   { get; set; } = string.Empty;
            public string InstallPath { get; set; } = string.Empty;
            public string ExePath     { get; set; } = string.Empty;
            public string Version     { get; set; } = string.Empty;
            public string SizeText    { get; set; } = string.Empty;
            public string Source      { get; set; } = string.Empty;
            public string IconPath    { get; set; } = string.Empty;
            public bool   HasIcon     { get; set; }

            private string _category = string.Empty;
            public string Category
            {
                get => _category;
                set { _category = value; OnPropertyChanged(); }
            }

            private bool _isLaunching;
            public bool IsLaunching
            {
                get => _isLaunching;
                set { _isLaunching = value; OnPropertyChanged(); }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
