using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Coclico.Services;

public class UserAccountService
{
    private static readonly string[] _imageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];

    public string UserName { get; private set; }
    public string DisplayName { get; private set; }
    public string Email { get; private set; }
    public bool IsMicrosoftAccount { get; private set; }
    public BitmapImage? Avatar { get; private set; }

    public UserAccountService()
    {
        UserName = "Guest";
        DisplayName = "Guest User";
        Email = "guest@local";
        LoadUserData();
    }

    public void LoadUserData()
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            UserName = identity.Name.Split('\\').Last();
            DisplayName = UserName;

            IsMicrosoftAccount = identity.Name.Contains("@");

            Email = IsMicrosoftAccount ? identity.Name.ToLower() : $"{UserName.ToLower()}@windows.local";

            LoadAvatar();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "UserAccountService.LoadUserData");
        }
    }

    private void LoadAvatar()
    {
        try
        {
            string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var identity = WindowsIdentity.GetCurrent();
            string userSid = identity.User?.Value ?? "";

            string[] possiblePaths =
            [
                Path.Combine(roamingAppData, @"Microsoft\Windows\AccountPictures"),
                Environment.ExpandEnvironmentVariables(@"%PUBLIC%\AccountPictures"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Packages", "Microsoft.Windows.ContentDeliveryManager_cw5n1h2txyewy", "LocalState", "Assets"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\User Account Pictures"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\User Account Pictures", userSid),
                Path.Combine(localAppData, @"Microsoft\Windows\CloudExperienceHost")
            ];

            string? bestPicture = null;
            DateTime latestTime = DateTime.MinValue;

            foreach (string path in possiblePaths)
            {
                if (Directory.Exists(path))
                {
                    foreach (string file in Directory.EnumerateFiles(path))
                    {
                        if (!_imageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                        {
                            continue;
                        }

                        var info = new FileInfo(file);
                        if (info.Length > 10000 && info.LastWriteTime > latestTime)
                        {
                            latestTime = info.LastWriteTime;
                            bestPicture = file;
                        }
                    }
                }
            }

            if (bestPicture != null)
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(bestPicture);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                try
                {
                    bitmap.EndInit();
                    Avatar = bitmap;
                }
                catch (Exception ex)
                {
                    LoggingService.LogException(ex, "UserAccountService.LoadAvatar");
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "UserAccountService.LoadAvatar.Global");
        }
    }

    public void OpenSettings(string page)
    {
        try
        {
            _ = Process.Start(new ProcessStartInfo($"ms-settings:{page}") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _ = MessageBox.Show($"Could not open settings: {ex.Message}");
        }
    }

    public void SetCustomAvatar(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("File not found", filePath);
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string avatarsDir = Path.Combine(appData, "Coclico", "avatars");
            _ = Directory.CreateDirectory(avatarsDir);
            string dest = Path.Combine(avatarsDir, UserName + Path.GetExtension(filePath).ToLower());

            File.Copy(filePath, dest, true);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(dest);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.EndInit();
            Avatar = bmp;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "UserAccountService.SetCustomAvatar");
            throw;
        }
    }
}
