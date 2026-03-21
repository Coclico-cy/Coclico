using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Coclico.Services
{
    public class AppProfile
    {
        public string Name { get; set; } = "Default";

        public string Description { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastModified { get; set; } = DateTime.UtcNow;

        public AppSettings Settings { get; set; } = new();

        public List<string> Categories { get; set; } = new();

        public List<InstalledProgramsService.FilterGroup> FilterGroups { get; set; } = new();

        public string AvatarInitials => string.IsNullOrEmpty(Name) ? "?" : Name[0].ToString().ToUpper();

        public DateTime LastUsed => LastModified;
    }

    public class ProfileService
    {
        private static ProfileService? _instance;
        public static ProfileService Instance => _instance ??= new ProfileService();

        private static readonly string ProfilesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico", "profiles");

        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        private ProfileService()
        {
            Directory.CreateDirectory(ProfilesDir);
        }

        public List<AppProfile> GetAllProfiles()
        {
            var result = new List<AppProfile>();
            try
            {
                foreach (var file in Directory.GetFiles(ProfilesDir, "*.json").OrderBy(f => f))
                {
                    var p = Load(Path.GetFileNameWithoutExtension(file));
                    if (p != null) result.Add(p);
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "ProfileService.GetAllProfiles");
            }
            return result;
        }

        public AppProfile? Load(string name)
        {
            try
            {
                var path = GetPath(name);
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<AppProfile>(File.ReadAllText(path));
            }
            catch (Exception ex) { LoggingService.LogException(ex, "ProfileService.Load"); return null; }
        }

        public async Task<AppProfile?> LoadAsync(string name)
        {
            try
            {
                var path = GetPath(name);
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<AppProfile>(await File.ReadAllTextAsync(path));
            }
            catch (Exception ex) { LoggingService.LogException(ex, "ProfileService.LoadAsync"); return null; }
        }

        public void Save(AppProfile profile)
        {
            try
            {
                profile.LastModified = DateTime.UtcNow;
                File.WriteAllText(GetPath(profile.Name), JsonSerializer.Serialize(profile, _opts));
            }
            catch (Exception ex) { LoggingService.LogException(ex, "ProfileService.Save"); }
        }

        public async Task SaveAsync(AppProfile profile)
        {
            try
            {
                profile.LastModified = DateTime.UtcNow;
                await File.WriteAllTextAsync(GetPath(profile.Name), JsonSerializer.Serialize(profile, _opts));
            }
            catch (Exception ex) { LoggingService.LogException(ex, "ProfileService.SaveAsync"); }
        }

        public void Delete(string name)
        {
            try { File.Delete(GetPath(name)); } catch (Exception ex) { LoggingService.LogException(ex, "ProfileService.Delete"); }
        }

        public void Rename(string oldName, string newName)
        {
            try
            {
                var profile = Load(oldName);
                if (profile == null) return;
                Delete(oldName);
                profile.Name = newName;
                Save(profile);
            }
            catch (Exception ex) { LoggingService.LogException(ex, "ProfileService.Rename"); }
        }

        public AppProfile Snapshot(string name, InstalledProgramsService svc)
        {
            var settingsCopy = JsonSerializer.Deserialize<AppSettings>(
                JsonSerializer.Serialize(SettingsService.Instance.Settings)) ?? new AppSettings();

            return new AppProfile
            {
                Name = name,
                Settings = settingsCopy,
                Categories = svc.GetCategories(),
                FilterGroups = svc.GetFilterGroups(),
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow
            };
        }

        private string GetPath(string name) =>
            Path.Combine(ProfilesDir, name.Replace(' ', '_') + ".json");
    }
}
