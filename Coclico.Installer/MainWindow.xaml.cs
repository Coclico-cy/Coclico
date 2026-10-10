using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Coclico.Installer.Models;
using Coclico.Installer.Services;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace Coclico.Installer;

public partial class MainWindow : FluentWindow
{
    private readonly InstallConfig _config = new();
    private readonly DispatcherTimer _tipsTimer = new();
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

    private static readonly Brush StepperActiveBackground = new SolidColorBrush(Color.FromRgb(28, 24, 34));
    private static readonly Brush StepperInactiveNumber = new SolidColorBrush(Color.FromRgb(100, 100, 100));
    private static readonly Brush StepperInactiveLabel = new SolidColorBrush(Color.FromRgb(120, 120, 120));

    public MainWindow()
    {
        InitializeComponent();

        TxtBetaBadge.Text = $"BÊTA v{InstallService.AppVersion}";

        _tipsTimer.Interval = TimeSpan.FromSeconds(4);
        _tipsTimer.Tick += (s, e) =>
        {
            var tips = InstallerLocalization.CurrentLanguage == InstallerLanguage.French ? TipsFr : TipsEn;
            _tipIndex = (_tipIndex + 1) % tips.Length;
            TxtTip.Text = tips[_tipIndex];
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LoadLicenseText();

        ApplyLanguage(InstallerLocalization.CurrentLanguage);

        Task.Run(() => SystemRequirementService.CheckRequirements(_config.InstallPath))
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    return;
                }
                if (t.IsCompletedSuccessfully)
                {
                    var req = t.Result;
                    _config.HasDotNet10 = req.HasDotNet10;
                    _config.DotNetVersion = req.DotNet10Version;
                    _config.HasNvidiaGpu = req.HasNvidiaGpu;
                    _config.GpuName = req.GpuName;
                    _config.AvailableDiskSpaceBytes = req.AvailableDiskSpaceBytes;
                    _config.TargetDriveLetter = req.TargetDriveLetter;

                    Dispatcher.Invoke(() =>
                    {
                        UpdateRequirementsUI(req);
                    });
                }
            });

        GoToStep(1);

        InitializeAiModelComboBox();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current?.Shutdown();
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

            CmbAiModel.IsEnabled = ChkAiModel.IsChecked == true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    private void OnAiModelCheckChanged(object sender, RoutedEventArgs e)
    {
        CmbAiModel.IsEnabled = ChkAiModel.IsChecked == true;
        UpdateAiModelSizeDisplay();
        UpdateSpaceCalculations();
    }

    private void CmbAiModel_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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
            string licensePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "LICENSE.txt");
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
        catch
        {
            TxtLicenseBox.Text = "MIT License — Copyright (c) 2026 Coclico";
        }
    }

    private void UpdateRequirementsUI(SystemRequirementStatus req)
    {
        try
        {
            if (req.HasDotNet10)
            {
                TxtDotNetStatus.Text = string.Format(InstallerLocalization.Get("DotNetFound"), req.DotNet10Version);
                TxtDotNetBadge.Text = "OK";
                DotNetIcon.Symbol = SymbolRegular.CheckmarkCircle24;
                DotNetIcon.Foreground = (Brush)FindResource("AccentSuccessBrush");
            }
            else
            {
                TxtDotNetStatus.Text = InstallerLocalization.Get("DotNetMissing");
                TxtDotNetBadge.Text = "AUTONOME";
                TxtDotNetBadge.Foreground = (Brush)FindResource("AccentCyanBrush");
                DotNetIcon.Symbol = SymbolRegular.Info24;
                DotNetIcon.Foreground = (Brush)FindResource("AccentCyanBrush");
            }

            if (req.HasNvidiaGpu)
            {
                TxtGpuStatus.Text = string.Format(InstallerLocalization.Get("GpuNvidiaFound"), req.GpuName);
                TxtGpuBadge.Text = "CUDA PRÊT";
                GpuIcon.Symbol = SymbolRegular.CheckmarkCircle24;
                GpuIcon.Foreground = (Brush)FindResource("AccentSuccessBrush");
                ChkCuda.IsChecked = true;
            }
            else
            {
                TxtGpuStatus.Text = string.Format(InstallerLocalization.Get("GpuStandard"), req.GpuName);
                TxtGpuBadge.Text = "CPU";
                GpuIcon.Symbol = SymbolRegular.Desktop24;
                GpuIcon.Foreground = (Brush)FindResource("TextSecondaryBrush");
                ChkCuda.IsChecked = false;
            }

            double freeGb = Math.Round((double)req.AvailableDiskSpaceBytes / (1024 * 1024 * 1024), 1);
            TxtSpaceAvailable.Text = string.Format(InstallerLocalization.Get("SpaceAvailable"), req.TargetDriveLetter, freeGb);
            UpdateSpaceCalculations();
        }
        catch { }
    }

    private void UpdateSpaceCalculations()
    {
        _config.InstallCuda = ChkCuda.IsChecked == true;
        _config.InstallAiModel = ChkAiModel.IsChecked == true;
        int reqMb = _config.CalculateRequiredSpaceMb();
        TxtSpaceRequired.Text = string.Format(InstallerLocalization.Get("SpaceRequired"), reqMb);
    }

    private void ApplyLanguage(InstallerLanguage lang)
    {
        InstallerLocalization.CurrentLanguage = lang;

        if (lang == InstallerLanguage.French)
        {
            BtnLangFr.Background = (Brush)FindResource("AccentPrimaryBrush");
            BtnLangFr.Foreground = Brushes.White;
            BtnLangEn.Background = Brushes.Transparent;
            BtnLangEn.Foreground = (Brush)FindResource("TextSecondaryBrush");
        }
        else
        {
            BtnLangEn.Background = (Brush)FindResource("AccentPrimaryBrush");
            BtnLangEn.Foreground = Brushes.White;
            BtnLangFr.Background = Brushes.Transparent;
            BtnLangFr.Foreground = (Brush)FindResource("TextSecondaryBrush");
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
        TxtBetaNotice.Text = InstallerLocalization.Get("BetaNotice");
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

        ViewStepWelcome.Visibility = Visibility.Collapsed;
        ViewStepLicense.Visibility = Visibility.Collapsed;
        ViewStepLocation.Visibility = Visibility.Collapsed;
        ViewStepComponents.Visibility = Visibility.Collapsed;
        ViewStepSummary.Visibility = Visibility.Collapsed;
        ViewStepInstalling.Visibility = Visibility.Collapsed;
        ViewStepFinish.Visibility = Visibility.Collapsed;
        ViewStepUninstall.Visibility = Visibility.Collapsed;

        switch (step)
        {
            case 1:
                ViewStepWelcome.Visibility = Visibility.Visible;
                BtnBack.Visibility = Visibility.Collapsed;
                BtnNext.IsEnabled = true;
                break;
            case 2:
                ViewStepLicense.Visibility = Visibility.Visible;
                BtnBack.Visibility = Visibility.Visible;
                BtnNext.IsEnabled = ChkAcceptLicense.IsChecked == true;
                break;
            case 3:
                ViewStepLocation.Visibility = Visibility.Visible;
                TxtInstallPath.Text = _config.InstallPath;
                BtnBack.Visibility = Visibility.Visible;
                BtnNext.IsEnabled = true;
                UpdateSpaceCalculations();
                break;
            case 4:
                ViewStepComponents.Visibility = Visibility.Visible;
                BtnBack.Visibility = Visibility.Visible;
                BtnNext.IsEnabled = true;
                break;
            case 5:
                ViewStepSummary.Visibility = Visibility.Visible;
                BtnBack.Visibility = Visibility.Visible;
                BtnNext.IsEnabled = true;
                UpdateSummaryView();
                break;
            case 6:
                ViewStepInstalling.Visibility = Visibility.Visible;
                NavBar.Visibility = Visibility.Collapsed;
                StartInstallation();
                break;
            case 7:
                ViewStepFinish.Visibility = Visibility.Visible;
                NavBar.Visibility = Visibility.Collapsed;
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
                items[i].Background = StepperActiveBackground;
                nums[i].Foreground = (Brush)FindResource("AccentCyanBrush");
                nums[i].Text = stepIndex.ToString();
                labels[i].Foreground = (Brush)FindResource("TextPrimaryBrush");
                labels[i].FontWeight = FontWeights.Bold;
            }
            else if (stepIndex < activeStep)
            {
                items[i].Background = Brushes.Transparent;
                nums[i].Foreground = (Brush)FindResource("AccentSuccessBrush");
                nums[i].Text = "✓";
                labels[i].Foreground = (Brush)FindResource("TextSecondaryBrush");
                labels[i].FontWeight = FontWeights.Normal;
            }
            else
            {
                items[i].Background = Brushes.Transparent;
                nums[i].Foreground = StepperInactiveNumber;
                nums[i].Text = stepIndex.ToString();
                labels[i].Foreground = StepperInactiveLabel;
                labels[i].FontWeight = FontWeights.Normal;
            }
        }
    }

    private void UpdateNextButtonText()
    {
        if (_currentStep == 5)
        {
            BtnNext.Content = InstallerLocalization.Get("BtnInstall");
        }
        else
        {
            BtnNext.Content = InstallerLocalization.Get("BtnNext");
        }
    }

    private void UpdateSummaryView()
    {
        TxtSummaryPath.Text = _config.InstallPath;

        var compList = new StringBuilder();
        compList.AppendLine("• Application Coclico Core");
        if (ChkCuda.IsChecked == true)
            compList.AppendLine("• Runtime NVIDIA CUDA 12 (~1,2 Go — téléchargé à la demande)");
        if (ChkAiModel.IsChecked == true)
            compList.AppendLine("• Modèle IA Local Coclico Copilot (~1.1 Go)");

        if (ChkDesktop.IsChecked == true)
            compList.AppendLine("• Raccourci sur le Bureau");
        if (ChkStartMenu.IsChecked == true)
            compList.AppendLine("• Entrée dans le Menu Démarrer");

        TxtSummaryComponentsList.Text = compList.ToString().TrimEnd();
        TxtSummaryTotalSpace.Text = string.Format(InstallerLocalization.Get("SpaceRequired"), _config.CalculateRequiredSpaceMb());
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
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

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1)
        {
            GoToStep(_currentStep - 1);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ChkAcceptLicense_Changed(object sender, RoutedEventArgs e)
    {
        if (_currentStep == 2)
        {
            BtnNext.IsEnabled = ChkAcceptLicense.IsChecked == true;
        }
    }

    private void OnComponentCheckChanged(object sender, RoutedEventArgs e)
    {
        UpdateSpaceCalculations();
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Dossier d'installation de Coclico",
            InitialDirectory = _config.InstallPath
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            _config.InstallPath = Path.Combine(dialog.FolderName, "Coclico");
            TxtInstallPath.Text = _config.InstallPath;
            UpdateRequirementsUI(SystemRequirementService.CheckRequirements(_config.InstallPath));
        }
    }

    private async void StartInstallation()
    {
        _config.InstallPath = TxtInstallPath.Text;
        _config.CreateDesktopShortcut = ChkDesktop.IsChecked == true;
        _config.CreateStartMenuShortcut = ChkStartMenu.IsChecked == true;
        _config.AutoLaunchOnFinish = ChkAutoLaunch.IsChecked == true;
        _config.InstallCuda = ChkCuda.IsChecked == true;
        _config.InstallAiModel = ChkAiModel.IsChecked == true;

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
        catch (Exception ex)
        {
            _tipsTimer.Stop();
            NavBar.Visibility = Visibility.Visible;
            System.Windows.MessageBox.Show(
                $"Erreur d'installation : {ex.Message}",
                "Installation Coclico",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);

            GoToStep(5);
        }
    }

    private void LaunchInstalledApp()
    {
        try
        {
            string exe = Path.Combine(_config.InstallPath, "Coclico.exe");
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
        catch { }
    }

    private void BtnLaunchApp_Click(object sender, RoutedEventArgs e)
    {
        LaunchInstalledApp();
        Close();
    }

    private void BtnFinishClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnLangFr_Click(object sender, RoutedEventArgs e)
    {
        ApplyLanguage(InstallerLanguage.French);
    }

    private void BtnLangEn_Click(object sender, RoutedEventArgs e)
    {
        ApplyLanguage(InstallerLanguage.English);
    }

    private async void BtnDoUninstall_Click(object sender, RoutedEventArgs e)
    {
        UninstallButtonPanel.Visibility = Visibility.Collapsed;
        UninstallProgress.Visibility = Visibility.Visible;
        TxtUninstallDesc.Text = "Désinstallation en cours...";

        var progress = new Progress<InstallProgressReport>(report =>
        {
            UninstallProgress.Value = report.Percent;
            TxtUninstallDesc.Text = report.Status;
        });

        try
        {
            await InstallService.UninstallAsync(progress);
            UninstallProgress.Visibility = Visibility.Collapsed;
            TxtUninstallTitle.Text = InstallerLocalization.Get("UninstallFinished");
            TxtUninstallDesc.Text = "Tous les fichiers et raccourcis ont été supprimés.";
            BtnFinishUninstalled.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            UninstallProgress.Visibility = Visibility.Collapsed;
            UninstallButtonPanel.Visibility = Visibility.Visible;
            TxtUninstallDesc.Text = $"Erreur : {ex.Message}";
        }
    }
}
