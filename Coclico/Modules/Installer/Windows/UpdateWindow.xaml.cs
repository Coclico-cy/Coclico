using System.IO;
using System.Windows;
using Coclico.Services;
using Wpf.Ui.Controls;

namespace Coclico.Modules.Installer.Windows;

public partial class UpdateWindow : FluentWindow
{
    private readonly GitHubRelease _release;
    private readonly UpdateManager _updateManager;
    private readonly SettingsService _settings;
    private bool _downloading;

    public UpdateWindow(GitHubRelease release, string currentVersion)
    {
        InitializeComponent();
        _release = release ?? throw new ArgumentNullException(nameof(release));
        _settings = ServiceContainer.GetRequired<SettingsService>();
        _updateManager = ServiceContainer.GetRequired<UpdateManager>();

        VersionLine.Text = $"v{currentVersion}  →  {_release.TagName}";
        NotesText.Text = string.IsNullOrWhiteSpace(_release.Body)
            ? "Pas de notes de version pour cette mise à jour."
            : _release.Body.Trim();
    }

    private async void BtnInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_downloading)
        {
            return;
        }

        GitHubAsset? asset = UpdateManager.PickSetupAsset(_release);
        if (asset == null)
        {
            StatusText.Text = "Installeur introuvable dans cette release. Télécharge la mise à jour depuis la page GitHub.";
            return;
        }

        _downloading = true;
        BtnInstall.IsEnabled = false;
        BtnLater.IsEnabled = false;
        BtnSkip.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Maximum = Math.Max(asset.Size, 1);
        DownloadProgress.Value = 0;
        StatusText.Text = "Téléchargement de la mise à jour...";

        string savePath = Path.Combine(Path.GetTempPath(), asset.Name);
        var progress = new Progress<long>(bytes =>
        {
            DownloadProgress.Value = bytes;
            StatusText.Text = $"Téléchargement : {bytes / 1048576.0:0.0} / {asset.Size / 1048576.0:0.0} Mo";
        });

        try
        {
            bool downloaded = await _updateManager.DownloadReleaseAsync(_release, asset.Name, savePath, progress);
            if (!downloaded)
            {
                StatusText.Text = "Échec du téléchargement ou de la vérification SHA-256. Réessayez plus tard.";
                ResetUi();
                return;
            }

            bool launched = _updateManager.LaunchInstaller(savePath, _release.TagName);
            if (!launched)
            {
                StatusText.Text = "L'installeur n'a pas pu être lancé. Lance-le manuellement depuis ton dossier Temp.";
                ResetUi();
                return;
            }

            StatusText.Text = "Installeur vérifié (SHA-256 + version) et démarré — Coclico va se fermer pour terminer la mise à jour.";
            await Task.Delay(1200);
            ExitForUpdate();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "UpdateWindow.Install");
            StatusText.Text = "Erreur pendant la mise à jour. Réessayez plus tard.";
            ResetUi();
        }
    }

    private void ResetUi()
    {
        _downloading = false;
        BtnInstall.IsEnabled = true;
        BtnLater.IsEnabled = true;
        BtnSkip.IsEnabled = true;
        DownloadProgress.Visibility = Visibility.Collapsed;
    }

    private void BtnLater_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnSkip_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.Settings.SkippedUpdateVersion = _release.TagName;
            _settings.Save();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "UpdateWindow.Skip");
        }

        Close();
    }

    private static void ExitForUpdate()
    {
        try
        {
            ServiceContainer.GetRequired<SettingsService>().Settings.MinimizeToTray = false;
        }
        catch
        {
        }

        Application.Current.Shutdown();
    }
}