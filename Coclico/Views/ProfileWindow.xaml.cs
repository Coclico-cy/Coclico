#nullable enable
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Coclico.Services;

namespace Coclico.Views;

public partial class ProfileWindow : Window
{
    private readonly UserAccountService _userService;

    public ProfileWindow(UserAccountService userService)
    {
        InitializeComponent();
        _userService = userService;
        DataContext = _userService;
        InitializeUI();
    }

    private void InitializeUI()
    {
        UserNameText.Text = _userService.DisplayName;
        UserEmailText.Text = _userService.Email;
        AccountTypeText.Text = _userService.IsMicrosoftAccount ? "Microsoft Account" : "Local Windows Account";

        if (_userService.Avatar != null)
        {
            UserAvatarImage.Source = _userService.Avatar;
            UserInitialText.Visibility = Visibility.Collapsed;
        }
        else
        {
            UserInitialText.Text = _userService.DisplayName is { Length: > 0 } n ? n[0].ToString().ToUpper() : "?";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void OpenInfo_Click(object sender, RoutedEventArgs e)
    {
        _userService.OpenSettings("yourinfo");
    }

    private void OpenAccounts_Click(object sender, RoutedEventArgs e)
    {
        _userService.OpenSettings("emailandaccounts");
    }

    private void Avatar_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog();
        dlg.Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*";
        if (dlg.ShowDialog(this) == true)
        {
            try
            {
                var crop = new AvatarCropWindow(dlg.FileName) { Owner = this };
                var ok = crop.ShowDialog();
                if (ok == true && !string.IsNullOrEmpty(crop.ResultFilePath))
                {
                    _userService.SetCustomAvatar(crop.ResultFilePath);
                    try { File.Delete(crop.ResultFilePath); } catch { }

                    if (_userService.Avatar != null)
                    {
                        UserAvatarImage.Source = _userService.Avatar;
                        UserInitialText.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "ProfileWindow.BtnChangeAvatar_Click");
                MessageBox.Show($"Impossible de charger l'image : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
