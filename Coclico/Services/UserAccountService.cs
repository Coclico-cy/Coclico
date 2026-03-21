using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Coclico.Services
{
    public class UserAccountService
    {
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
                
                if (IsMicrosoftAccount)
                {
                    Email = identity.Name.ToLower();
                }
                else
                {
                    Email = $"{UserName.ToLower()}@windows.local";
                }

                LoadAvatar();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading user data: {ex.Message}");
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
                
                string[] possiblePaths = new[]
                {
                    Path.Combine(roamingAppData, @"Microsoft\Windows\AccountPictures"),
                    Path.Combine(localAppData, @"Microsoft\Windows\AccountPictures"),
                    Environment.ExpandEnvironmentVariables(@"%PUBLIC%\AccountPictures"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Packages", "Microsoft.Windows.ContentDeliveryManager_cw5n1h2txyewy", "LocalState", "Assets"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\User Account Pictures"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\User Account Pictures", userSid),
                    Path.Combine(localAppData, @"Microsoft\Windows\CloudExperienceHost"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\AccountPictures")
                };

                string? bestPicture = null;
                DateTime latestTime = DateTime.MinValue;

                foreach (var path in possiblePaths)
                {
                    if (Directory.Exists(path))
                    {
                        var files = Directory.GetFiles(path, "*");
                        foreach (var file in files)
                        {
                            var info = new FileInfo(file);
                            if (info.Length > 10000)
                            {
                                if (info.LastWriteTime > latestTime)
                                {
                                    latestTime = info.LastWriteTime;
                                    bestPicture = file;
                                }
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
                    catch 
                    {
                        Debug.WriteLine($"Failed to load potential avatar: {bestPicture}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading avatar: {ex.Message}");
            }
        }

        public void OpenSettings(string page)
        {
            try
            {
                Process.Start(new ProcessStartInfo($"ms-settings:{page}") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open settings: {ex.Message}");
            }
        }

        public void SetCustomAvatar(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) throw new FileNotFoundException("File not found", filePath);

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string avatarsDir = Path.Combine(appData, "Coclico", "avatars");
                Directory.CreateDirectory(avatarsDir);
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
                Debug.WriteLine($"Failed to set custom avatar: {ex.Message}");
                throw;
            }
        }
    }
}
