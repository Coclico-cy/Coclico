using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Coclico.Installer.Models;
using Coclico.Installer.Services;
using Microsoft.Win32;

namespace Coclico.Installer.Views;

public partial class UninstallWindow : Window
{
    private readonly string _installPath;
    private CancellationTokenSource? _cts;

    public UninstallWindow()
    {
        InitializeComponent();

        _installPath = GetInstallationPath();

        TxtUnTitle.Text = InstallerLocalization.Get("UnTitle");
        TxtUnSubtitle.Text = InstallerLocalization.Get("UnSubtitle");
        TxtUnWarning.Text = InstallerLocalization.Get("UnWarning");
        TxtUnListTitle.Text = InstallerLocalization.Get("UnListTitle");
        TxtUnItem1.Text = InstallerLocalization.Get("UnItem1");
        TxtUnItem2.Text = InstallerLocalization.Get("UnItem2");
        TxtUnItem3.Text = InstallerLocalization.Get("UnItem3");
        TxtUnItem4.Text = InstallerLocalization.Get("UnItem4");
        TxtUnItem5.Text = InstallerLocalization.Get("UnItem5");
        TxtProgressStatus.Text = InstallerLocalization.Get("UnReady");
        ChkConfirmCompleteRemoval.Content = InstallerLocalization.Get("UnCheck");
        ChkKeepUserData.Content = InstallerLocalization.Get("UnKeepData");
        BtnCancelUninstall.Content = InstallerLocalization.Get("UnBtnCancel");
        BtnStartUninstall.Content = InstallerLocalization.Get("UnBtnUninstall");
        TxtUnByeTitle.Text = InstallerLocalization.Get("UnByeTitle");
        TxtUnByeDesc.Text = InstallerLocalization.Get("UnByeDesc");
        TxtUnByeFooter.Text = InstallerLocalization.Get("UnByeFooter");

        TxtInstallPath.Text = string.IsNullOrEmpty(_installPath)
            ? InstallerLocalization.Get("UnNotFound")
            : _installPath;
    }

    private void TitleBar_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.Source is Avalonia.Visual src &&
            (src is Button || src.GetVisualAncestors().Any(v => v is Button)))
        {
            return;
        }

        BeginMoveDrag(e);
    }

    private void BtnTitleClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private string GetInstallationPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Coclico");
            if (key != null)
            {
                var installLocation = key.GetValue("InstallLocation") as string;
                if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                {
                    return installLocation;
                }
            }

            string currentExeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;

            if (!string.IsNullOrEmpty(currentExeDir) &&
                Path.GetFileName(currentExeDir.TrimEnd(Path.DirectorySeparatorChar))
                    .Equals("Uninstall", StringComparison.OrdinalIgnoreCase))
            {
                string? parentDir = Path.GetDirectoryName(currentExeDir.TrimEnd(Path.DirectorySeparatorChar));
                if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                {
                    return parentDir;
                }
            }

            if (!string.IsNullOrEmpty(currentExeDir) && Directory.Exists(currentExeDir))
            {
                return currentExeDir;
            }

            string programFilesPath = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Coclico");
            if (Directory.Exists(programFilesPath))
            {
                return programFilesPath;
            }

            string localAppDataPath = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Coclico");
            if (Directory.Exists(localAppDataPath))
            {
                return localAppDataPath;
            }

            return string.Empty;
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Security.SecurityException)
        {
            return string.Empty;
        }
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnStartUninstall_Click(object? sender, RoutedEventArgs e)
        => RunSafely(BtnStartUninstall_ClickAsync, "UninstallWindow.BtnStartUninstall_Click");

    private async Task BtnStartUninstall_ClickAsync()
    {
        if (ChkConfirmCompleteRemoval.IsChecked is not true)
        {
            ChkConfirmCompleteRemoval.Focus();
            await MessageDialog.ShowWarningAsync(this,
                InstallerLocalization.Get("UnConfirmTitle"),
                InstallerLocalization.Get("UnConfirmMsg"));
            return;
        }

        BtnStartUninstall.IsEnabled = false;
        BtnCancelUninstall.IsEnabled = false;
        ChkConfirmCompleteRemoval.IsEnabled = false;
        ChkKeepUserData.IsEnabled = false;

        TxtProgressStatus.Text = InstallerLocalization.Get("UnStarting");
        using CancellationTokenSource cts = new();
        _cts = cts;

        try
        {
            var progress = new Progress<InstallProgressReport>(report =>
            {
                UninstallProgressBar.Value = report.Percent;
                TxtProgressPercent.Text = ((int)report.Percent) + "%";
                TxtProgressStatus.Text = report.Status;
            });

            string? targetPath = string.IsNullOrEmpty(_installPath) ? null : _installPath;
            await InstallService.FullUninstallAsync(progress, targetPath, ChkKeepUserData.IsChecked is true, _cts.Token);

            PanelMain.IsVisible = false;
            PanelGoodbye.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
            TxtProgressStatus.Text = InstallerLocalization.Get("UnCancelled");
            BtnStartUninstall.IsEnabled = true;
            BtnCancelUninstall.IsEnabled = true;
            ChkConfirmCompleteRemoval.IsEnabled = true;
            ChkKeepUserData.IsEnabled = true;
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            TxtProgressStatus.Text = "Erreur : " + ex.Message;
            BtnStartUninstall.IsEnabled = true;
            BtnCancelUninstall.IsEnabled = true;
            ChkConfirmCompleteRemoval.IsEnabled = true;
            ChkKeepUserData.IsEnabled = true;
        }
        finally
        {
            _cts = null;
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
            System.Diagnostics.Debug.WriteLine($"[UninstallWindow] {operationName} : {ex.Message}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        base.OnClosed(e);
    }
}
