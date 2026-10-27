using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Coclico.Installer.Models;
using Coclico.Installer.Services;

namespace Coclico.Installer;

public partial class MainWindow : Window
{
    private readonly InstallConfig _config = new();
    private readonly DispatcherTimer _tipsTimer;
    private int _tipIndex = 0;
    private int _currentStep = 1;

    private static readonly string[] TipsFr =
    [
        "Nettoyage Intelligent : Libérez de la mémoire RAM et les caches temporaires en un clic.",
        "IA Locale : Assistant Copilot autonome fonctionnant sans cloud ni compte requis.",
        "Surveillance Matérielle : Suivi précis du CPU, GPU, RAM et températures en temps réel.",
        "Maintenance Système : Réparation DISM, SFC et gestion des services Windows intégrés."
    ];

    private static readonly string[] TipsEn =
    [
        "Smart Cleaning: Free up RAM and temporary system caches with a single click.",
        "Local AI: Autonomous Copilot companion running without any cloud or account needed.",
        "Hardware Monitor: Real-time telemetry for CPU, GPU, RAM and thermal metrics.",
        "System Maintenance: DISM, SFC scan and Windows services manager built-in."
    ];

    public MainWindow()
    {
        InitializeComponent();

        TxtHeaderTitle.Text = $"Coclico v{InstallService.AppVersion}";

        _tipsTimer = new DispatcherTimer(TimeSpan.FromSeconds(4), DispatcherPriority.Default, (s, e) => OnTipsTimerTick());
    }

    private void OnTipsTimerTick()
    {
        var tips = InstallerLocalization.CurrentLanguage == InstallerLanguage.French ? TipsFr : TipsEn;
        _tipIndex = (_tipIndex + 1) % tips.Length;
        TxtTip.Text = tips[_tipIndex];
    }

    private void Window_Opened(object? sender, EventArgs e)
    {
        LoadLicenseText();

        ApplyLanguage(InstallerLocalization.CurrentLanguage);

        _ = Task.Run(() => SystemRequirementService.CheckRequirements(_config.InstallPath))
            .ContinueWith(t =>
            {
                if (t.IsFaulted || !t.IsCompletedSuccessfully)
                {
                    return;
                }

                var req = t.Result;
                _config.HasDotNet10 = req.HasDotNet10;
                _config.DotNetVersion = req.DotNet10Version;
                _config.HasNvidiaGpu = req.HasNvidiaGpu;
                _config.GpuName = req.GpuName;
                _config.AvailableDiskSpaceBytes = req.AvailableDiskSpaceBytes;
                _config.TargetDriveLetter = req.TargetDriveLetter;

                Dispatcher.UIThread.Post(() => UpdateRequirementsUI(req));
            });

        GoToStep(1);

        InitializeAiModelComboBox();
    }

    private void TitleBar_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        if (e.Source is Visual src && IsInsideInteractive(src))
        {
            return;
        }

        BeginMoveDrag(e);
    }

    private static bool IsInsideInteractive(Visual v)
    {
        Visual? cur = v;
        while (cur is not null)
        {
            if (cur is Button or ComboBox or TextBox)
            {
                return true;
            }
            cur = cur.GetVisualParent();
        }
        return false;
    }

    private void BtnMinimize_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void InitializeAiModelComboBox()
    {
        try
        {
            CmbAiModel.ItemsSource = InstallerAiCatalog.Models;
            CmbAiModel.SelectedIndex = 0;

            if (InstallerAiCatalog.Models.Count > 0)
            {
                _config.SelectedAiModel = InstallerAiCatalog.Models[0];
            }

            UpdateAiModelSizeDisplay();

            CmbAiModel.IsEnabled = ChkAiModel.IsChecked is true;
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    private void OnAiModelCheckChanged(object? sender, RoutedEventArgs e)
    {
        CmbAiModel.IsEnabled = ChkAiModel.IsChecked is true;
        UpdateAiModelSizeDisplay();
        UpdateSpaceCalculations();
    }

    private void CmbAiModel_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateAiModelSizeDisplay();
        UpdateSpaceCalculations();
    }

    private void UpdateAiModelSizeDisplay()
    {
        if (CmbAiModel.SelectedItem is InstallerAiModel selectedModel)
        {
            TxtAiModelSize.Text = $"~{selectedModel.SizeMb} Mo";
            _config.SelectedAiModel = selectedModel;
        }
    }

    private void LoadLicenseText()
    {
        try
        {
            string licensePath = Path.Join(AppContext.BaseDirectory, "..", "..", "..", "..", "LICENSE.txt");
            if (File.Exists(licensePath))
            {
                TxtLicenseBox.Text = File.ReadAllText(licensePath);
                return;
            }

            TxtLicenseBox.Text =
                "MIT License\n\nCopyright (c) 2026 Coclico contributors\n\n" +
                "Permission is hereby granted, free of charge, to any person obtaining a copy\n" +
                "of this software and associated documentation files (the \"Software\"), to deal\n" +
                "in the Software without restriction, including without limitation the rights\n" +
                "to use, copy, modify, merge, publish, distribute, sublicense, and/or sell\n" +
                "copies of the Software, and to permit persons to whom the Software is\n" +
                "furnished to do so, subject to the following conditions:\n\n" +
                "The above copyright notice and this permission notice shall be included in all\n" +
                "copies or substantial portions of the Software.\n\n" +
                "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR\n" +
                "IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,\n" +
                "FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            TxtLicenseBox.Text = "MIT License - Copyright (c) 2026 Coclico";
        }
    }

    private void UpdateRequirementsUI(SystemRequirementStatus req)
    {
        try
        {
            if (req.HasDotNet10)
            {
                TxtDotNetStatus.Text = TextFormatService.Format(InstallerLocalization.Get("DotNetFound"), req.DotNet10Version);
                TxtDotNetBadge.Text = "OK";
                DotNetIcon.Data = Ico.CheckCircle;
                DotNetIcon.Foreground = AppBrushes.AccentSuccess;
            }
            else
            {
                TxtDotNetStatus.Text = InstallerLocalization.Get("DotNetMissing");
                TxtDotNetBadge.Text = "AUTONOME";
                TxtDotNetBadge.Foreground = AppBrushes.AccentCyan;
                DotNetIcon.Data = Ico.Info;
                DotNetIcon.Foreground = AppBrushes.AccentCyan;
            }

            if (req.HasNvidiaGpu)
            {
                TxtGpuStatus.Text = TextFormatService.Format(InstallerLocalization.Get("GpuNvidiaFound"), req.GpuName);
                TxtGpuBadge.Text = "CUDA PRÊT";
                GpuIcon.Data = Ico.CheckCircle;
                GpuIcon.Foreground = AppBrushes.AccentSuccess;
                ChkCuda.IsChecked = true;
            }
            else
            {
                TxtGpuStatus.Text = TextFormatService.Format(InstallerLocalization.Get("GpuStandard"), req.GpuName);
                TxtGpuBadge.Text = "CPU";
                GpuIcon.Data = Ico.Desktop;
                GpuIcon.Foreground = AppBrushes.TextSecondary;
                ChkCuda.IsChecked = false;
            }

            double freeGb = Math.Round((double)req.AvailableDiskSpaceBytes / (1024 * 1024 * 1024), 1);
            TxtSpaceAvailable.Text = TextFormatService.Format(InstallerLocalization.Get("SpaceAvailable"), req.TargetDriveLetter, freeGb);
            UpdateSpaceCalculations();
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { _ = ex; }
    }

    private void UpdateSpaceCalculations()
    {
        _config.InstallCuda = ChkCuda.IsChecked is true;
        _config.InstallAiModel = ChkAiModel.IsChecked is true;
        int reqMb = _config.CalculateRequiredSpaceMb();
        TxtSpaceRequired.Text = TextFormatService.Format(InstallerLocalization.Get("SpaceRequired"), reqMb);
    }

    private void ApplyLanguage(InstallerLanguage lang)
    {
        InstallerLocalization.CurrentLanguage = lang;

        if (lang == InstallerLanguage.French)
        {
            BtnLangFr.Background = AppBrushes.AccentPrimary;
            BtnLangFr.Foreground = AppBrushes.White;
            BtnLangEn.Background = AppBrushes.Transparent;
            BtnLangEn.Foreground = AppBrushes.TextSecondary;
        }
        else
        {
            BtnLangEn.Background = AppBrushes.AccentPrimary;
            BtnLangEn.Foreground = AppBrushes.White;
            BtnLangFr.Background = AppBrushes.Transparent;
            BtnLangFr.Foreground = AppBrushes.TextSecondary;
        }

        BtnBack.Content = InstallerLocalization.Get("BtnBack");
        BtnCancel.Content = InstallerLocalization.Get("BtnCancel");
        BtnBrowse.Content = InstallerLocalization.Get("BtnBrowse");

        StepLabel1.Text = InstallerLocalization.Get("StepWelcome");
        StepLabel2.Text = InstallerLocalization.Get("StepLicense");
        StepLabel3.Text = InstallerLocalization.Get("StepLocation");
        StepLabel4.Text = InstallerLocalization.Get("StepComponents");
        StepLabel5.Text = InstallerLocalization.Get("StepSummary");
        StepLabel6.Text = InstallerLocalization.Get("StepInstall");
        StepLabel7.Text = InstallerLocalization.Get("StepFinish");

        TxtWelcomeTitle.Text = InstallerLocalization.Get("WelcomeTitle");
        TxtWelcomeDesc.Text = InstallerLocalization.Get("WelcomeDesc");
        TxtDotNetTitle.Text = InstallerLocalization.Get("DotNetCheckTitle");
        TxtGpuTitle.Text = InstallerLocalization.Get("GpuCheckTitle");

        TxtLicenseTitle.Text = InstallerLocalization.Get("LicenseTitle");
        TxtLicenseDesc.Text = InstallerLocalization.Get("LicenseDesc");
        ChkAcceptLicense.Content = InstallerLocalization.Get("LicenseAccept");

        TxtLocationTitle.Text = InstallerLocalization.Get("LocationTitle");
        TxtLocationDesc.Text = InstallerLocalization.Get("LocationDesc");
        TxtLocationPrivacy.Text = InstallerLocalization.Get("LocationPrivacyNote");

        TxtComponentsTitle.Text = InstallerLocalization.Get("ComponentsTitle");
        TxtComponentsDesc.Text = InstallerLocalization.Get("ComponentsDesc");
        TxtCompCoreTitle.Text = InstallerLocalization.Get("CompCoreTitle");
        TxtCompCoreDesc.Text = InstallerLocalization.Get("CompCoreDesc");
        TxtCompCudaTitle.Text = InstallerLocalization.Get("CompCudaTitle");
        TxtCompCudaDesc.Text = InstallerLocalization.Get("CompCudaDesc");
        TxtCompAiTitle.Text = InstallerLocalization.Get("CompAiTitle");
        TxtCompAiDesc.Text = InstallerLocalization.Get("CompAiDesc");
        TxtOptionsGroupTitle.Text = InstallerLocalization.Get("OptionsGroupTitle");
        ChkDesktop.Content = InstallerLocalization.Get("OptDesktop");
        ChkStartMenu.Content = InstallerLocalization.Get("OptStartMenu");
        ChkAutoLaunch.Content = InstallerLocalization.Get("OptAutoLaunch");

        TxtSummaryTitle.Text = InstallerLocalization.Get("SummaryTitle");
        TxtSummaryDesc.Text = InstallerLocalization.Get("SummaryDesc");
        TxtSummaryDestLabel.Text = InstallerLocalization.Get("SummaryDestLabel");
        TxtSummaryComponentsLabel.Text = InstallerLocalization.Get("SummaryComponentsLabel");

        TxtInstallingTitle.Text = InstallerLocalization.Get("InstallingTitle");
        TxtInstallingDesc.Text = InstallerLocalization.Get("InstallingDesc");

        TxtFinishTitle.Text = InstallerLocalization.Get("FinishTitle");
        TxtFinishDesc.Text = InstallerLocalization.Get("FinishDesc");
        TxtFinishThanksTitle.Text = InstallerLocalization.Get("FinishThanksTitle");
        TxtFinishThanksDesc.Text = InstallerLocalization.Get("FinishThanksDesc");
        TxtFinishNote.Text = InstallerLocalization.Get("FinishNote");
        BtnLaunchApp.Content = InstallerLocalization.Get("BtnLaunch");
        BtnFinishClose.Content = InstallerLocalization.Get("BtnFinish");

        TxtUninstallTitle.Text = InstallerLocalization.Get("UninstallTitle");
        TxtUninstallDesc.Text = InstallerLocalization.Get("UninstallDesc");
        BtnDoUninstall.Content = InstallerLocalization.Get("UninstallBtn");

        UpdateRequirementsUI(new SystemRequirementStatus(
            _config.HasDotNet10, _config.DotNetVersion, _config.HasNvidiaGpu, _config.GpuName, _config.AvailableDiskSpaceBytes, _config.TargetDriveLetter));

        UpdateNextButtonText();
    }

    private void GoToStep(int step)
    {
        _currentStep = step;

        ViewStepWelcome.IsVisible = false;
        ViewStepLicense.IsVisible = false;
        ViewStepLocation.IsVisible = false;
        ViewStepComponents.IsVisible = false;
        ViewStepSummary.IsVisible = false;
        ViewStepInstalling.IsVisible = false;
        ViewStepFinish.IsVisible = false;
        ViewStepUninstall.IsVisible = false;

        switch (step)
        {
            case 1:
                ViewStepWelcome.IsVisible = true;
                BtnBack.IsVisible = false;
                BtnNext.IsEnabled = true;
                NavBar.IsVisible = true;
                break;
            case 2:
                ViewStepLicense.IsVisible = true;
                BtnBack.IsVisible = true;
                BtnNext.IsEnabled = ChkAcceptLicense.IsChecked is true;
                NavBar.IsVisible = true;
                break;
            case 3:
                ViewStepLocation.IsVisible = true;
                TxtInstallPath.Text = _config.InstallPath;
                BtnBack.IsVisible = true;
                BtnNext.IsEnabled = true;
                NavBar.IsVisible = true;
                UpdateSpaceCalculations();
                break;
            case 4:
                ViewStepComponents.IsVisible = true;
                BtnBack.IsVisible = true;
                BtnNext.IsEnabled = true;
                NavBar.IsVisible = true;
                break;
            case 5:
                ViewStepSummary.IsVisible = true;
                BtnBack.IsVisible = true;
                BtnNext.IsEnabled = true;
                NavBar.IsVisible = true;
                UpdateSummaryView();
                break;
            case 6:
                ViewStepInstalling.IsVisible = true;
                NavBar.IsVisible = false;
                StartInstallation();
                break;
            case 7:
                ViewStepFinish.IsVisible = true;
                NavBar.IsVisible = false;
                break;
        }

        UpdateStepperHighlight(step);
        UpdateNextButtonText();
    }

    private void UpdateStepperHighlight(int activeStep)
    {
        var items = new[] { StepItem1, StepItem2, StepItem3, StepItem4, StepItem5, StepItem6, StepItem7 };
        var nums = new[] { StepNum1, StepNum2, StepNum3, StepNum4, StepNum5, StepNum6, StepNum7 };
        var labels = new[] { StepLabel1, StepLabel2, StepLabel3, StepLabel4, StepLabel5, StepLabel6, StepLabel7 };

        for (int i = 0; i < items.Length; i++)
        {
            int stepIndex = i + 1;
            if (stepIndex == activeStep)
            {
                items[i].Background = AppBrushes.StepperActiveBackground;
                nums[i].Foreground = AppBrushes.AccentCyan;
                nums[i].Text = stepIndex.ToString();
                labels[i].Foreground = AppBrushes.TextPrimary;
                labels[i].FontWeight = FontWeight.Bold;
            }
            else if (stepIndex < activeStep)
            {
                items[i].Background = AppBrushes.Transparent;
                nums[i].Foreground = AppBrushes.AccentSuccess;
                nums[i].Text = "✓";
                labels[i].Foreground = AppBrushes.TextSecondary;
                labels[i].FontWeight = FontWeight.Normal;
            }
            else
            {
                items[i].Background = AppBrushes.Transparent;
                nums[i].Foreground = AppBrushes.StepperInactiveNumber;
                nums[i].Text = stepIndex.ToString();
                labels[i].Foreground = AppBrushes.StepperInactiveLabel;
                labels[i].FontWeight = FontWeight.Normal;
            }
        }
    }

    private void UpdateNextButtonText()
    {
        BtnNext.Content = _currentStep == 5
            ? InstallerLocalization.Get("BtnInstall")
            : InstallerLocalization.Get("BtnNext");
    }

    private void UpdateSummaryView()
    {
        TxtSummaryPath.Text = _config.InstallPath;

        var compList = new StringBuilder();
        compList.AppendLine("• Application Coclico Core");
        if (ChkCuda.IsChecked is true)
            compList.AppendLine("• Runtime NVIDIA CUDA 12 (~1,2 Go - téléchargé à la demande)");
        if (ChkAiModel.IsChecked is true)
            compList.AppendLine("• Modèle IA Local Coclico Copilot (~1.1 Go)");

        if (ChkDesktop.IsChecked is true)
            compList.AppendLine("• Raccourci sur le Bureau");
        if (ChkStartMenu.IsChecked is true)
            compList.AppendLine("• Entrée dans le Menu Démarrer");

        TxtSummaryComponentsList.Text = compList.ToString().TrimEnd();
        TxtSummaryTotalSpace.Text = TextFormatService.Format(InstallerLocalization.Get("SpaceRequired"), _config.CalculateRequiredSpaceMb());
    }

    private void BtnNext_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentStep == 5)
        {
            GoToStep(6);
        }
        else if (_currentStep < 5)
        {
            GoToStep(_currentStep + 1);
        }
    }

    private void BtnBack_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentStep > 1)
        {
            GoToStep(_currentStep - 1);
        }
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ChkAcceptLicense_Changed(object? sender, RoutedEventArgs e)
    {
        if (_currentStep == 2)
        {
            BtnNext.IsEnabled = ChkAcceptLicense.IsChecked is true;
        }
    }

    private void OnComponentCheckChanged(object? sender, RoutedEventArgs e)
    {
        UpdateSpaceCalculations();
    }

    private async void BtnBrowse_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Dossier d'installation de Coclico",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                string folderName = folders[0].Path.LocalPath;
                if (!string.IsNullOrWhiteSpace(folderName))
                {
                    _config.InstallPath = Path.Join(folderName, "Coclico");
                    TxtInstallPath.Text = _config.InstallPath;
                    UpdateRequirementsUI(SystemRequirementService.CheckRequirements(_config.InstallPath));
                }
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            Debug.WriteLine($"[MainWindow.Browse] {ex.Message}");
        }
    }
    private void StartInstallation()
        => RunSafely(StartInstallationAsync, "MainWindow.StartInstallation");

    private async Task StartInstallationAsync()
    {
        _config.InstallPath = TxtInstallPath.Text!;
        _config.CreateDesktopShortcut = ChkDesktop.IsChecked is true;
        _config.CreateStartMenuShortcut = ChkStartMenu.IsChecked is true;
        _config.AutoLaunchOnFinish = ChkAutoLaunch.IsChecked is true;
        _config.InstallCuda = ChkCuda.IsChecked is true;
        _config.InstallAiModel = ChkAiModel.IsChecked is true;

        _tipsTimer.Start();

        var progress = new Progress<InstallProgressReport>(report =>
        {
            ProgressBar.Value = report.Percent;
            TxtProgressPercent.Text = $"{(int)report.Percent}%";
            TxtProgressStatus.Text = report.Status;
            if (!string.IsNullOrEmpty(report.SubStatus))
            {
                TxtInstallingDesc.Text = report.SubStatus;
            }
        });

        try
        {
            await InstallService.InstallAsync(_config, progress);
            _tipsTimer.Stop();

            if (_config.AutoLaunchOnFinish)
            {
                LaunchInstalledApp();
                Close();
                return;
            }

            GoToStep(7);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            _tipsTimer.Stop();
            NavBar.IsVisible = true;
            await Views.MessageDialog.ShowErrorAsync(this,
                "Installation Coclico",
                $"Erreur d'installation : {ex.Message}");

            GoToStep(5);
        }
    }

    private void LaunchInstalledApp()
    {
        try
        {
            string exe = Path.Join(_config.InstallPath, "Coclico.exe");
            if (File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = _config.InstallPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.InvalidOperationException)
        {
            Debug.WriteLine($"[MainWindow.Installer] Lancement de Coclico après installation échoué : {ex.Message}");
        }
    }

    private void BtnLaunchApp_Click(object? sender, RoutedEventArgs e)
    {
        LaunchInstalledApp();
        Close();
    }

    private void BtnFinishClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnLangFr_Click(object? sender, RoutedEventArgs e)
    {
        ApplyLanguage(InstallerLanguage.French);
    }

    private void BtnLangEn_Click(object? sender, RoutedEventArgs e)
    {
        ApplyLanguage(InstallerLanguage.English);
    }

    private void BtnDoUninstall_Click(object? sender, RoutedEventArgs e)
        => RunSafely(() => BtnDoUninstall_ClickAsync(), "MainWindow.BtnDoUninstall_Click");

    private async Task BtnDoUninstall_ClickAsync()
    {
        UninstallButtonPanel.IsVisible = false;
        UninstallProgress.IsVisible = true;
        TxtUninstallDesc.Text = "Désinstallation en cours...";

        var progress = new Progress<InstallProgressReport>(report =>
        {
            UninstallProgress.Value = report.Percent;
            TxtUninstallDesc.Text = report.Status;
        });

        try
        {
            await InstallService.UninstallAsync(progress);
            UninstallProgress.IsVisible = false;
            TxtUninstallTitle.Text = InstallerLocalization.Get("UninstallFinished");
            TxtUninstallDesc.Text = "Tous les fichiers et raccourcis ont été supprimés.";
            BtnFinishUninstalled.IsVisible = true;
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            UninstallProgress.IsVisible = false;
            UninstallButtonPanel.IsVisible = true;
            TxtUninstallDesc.Text = $"Erreur : {ex.Message}";
        }
    }
    private static void RunSafely(Func<Task> handler, string operationName)
    {
        _ = RunSafelyAsync(handler, operationName);
    }

    private static async Task RunSafelyAsync(Func<Task> handler, string operationName)
    {
        try
        {
            await handler();
        }
        catch (OperationCanceledException ex) { _ = ex; }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            Debug.WriteLine($"[MainWindow.Installer] {operationName} : {ex.Message}");
        }
    }
}
