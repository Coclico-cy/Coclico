using System.IO;
using System.Text.Json;

namespace Coclico.Services;

public class AppProfile
{
    public string Name { get; set; } = "Default";

    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    public AppSettings Settings { get; set; } = new();

    public List<string> Categories { get; set; } = [];

    public List<InstalledProgramsService.FilterGroup> FilterGroups { get; set; } = [];

    public string AvatarInitials => string.IsNullOrEmpty(Name) ? "?" : Name[0].ToString().ToUpper();

    public DateTime LastUsed => LastModified;
}

public class ProfileService
{
    private static readonly string ProfilesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico", "profiles");

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    public ProfileService()
    {
        _ = Directory.CreateDirectory(ProfilesDir);
    }

    public List<AppProfile> GetAllProfiles()
    {
        var result = new List<AppProfile>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(ProfilesDir, "*.json").OrderBy(f => f))
            {
                AppProfile? profile = Load(Path.GetFileNameWithoutExtension(file));
                if (profile != null)
                {
                    result.Add(profile);
                }
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
            string path = GetPath(name);
            return !File.Exists(path) ? null : JsonSerializer.Deserialize<AppProfile>(File.ReadAllText(path));
        }
        catch (Exception ex) { LoggingService.LogException(ex, "ProfileService.Load"); return null; }
    }

    public async Task<AppProfile?> LoadAsync(string name)
    {
        try
        {
            string path = GetPath(name);
            return !File.Exists(path) ? null : JsonSerializer.Deserialize<AppProfile>(await File.ReadAllTextAsync(path));
        }
        catch (Exception ex) { LoggingService.LogException(ex, "ProfileService.LoadAsync"); return null; }
    }

    public void Save(AppProfile profile)
    {
        try
        {
            WriteProfileAtomically(GetPath(profile.Name), profile);
        }
        catch (Exception ex) { LoggingService.LogException(ex, "ProfileService.Save"); }
    }

    public async Task SaveAsync(AppProfile profile)
    {
        try
        {
            await WriteProfileAtomicallyAsync(GetPath(profile.Name), profile);
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
            AppProfile? profile = Load(oldName);
            if (profile == null)
            {
                return;
            }

            string oldPath = GetPath(oldName);
            string newPath = GetPath(newName);
            profile.Name = newName;
            WriteProfileAtomically(newPath, profile);
            if (File.Exists(oldPath) && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(oldPath);
            }
        }
        catch (Exception ex) { LoggingService.LogException(ex, "ProfileService.Rename"); }
    }

    public AppProfile Snapshot(string name, InstalledProgramsService svc)
    {
        AppSettings settingsCopy = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(ServiceContainer.GetRequired<SettingsService>().Settings)) ?? new AppSettings();

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

    private static void WriteProfileAtomically(string path, AppProfile profile)
    {
        profile.LastModified = DateTime.UtcNow;
        string tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(profile, _opts));
        File.Move(tempPath, path, overwrite: true);
    }

    private static async Task WriteProfileAtomicallyAsync(string path, AppProfile profile)
    {
        profile.LastModified = DateTime.UtcNow;
        string tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(profile, _opts));
        File.Move(tempPath, path, overwrite: true);
    }

    private string GetPath(string name)
    {
        string safeName = Path.GetFileName(name.Replace('/', '_').Replace('\\', '_'));
        return string.IsNullOrWhiteSpace(safeName)
            ? throw new ArgumentException("Invalid profile name.", nameof(name))
            : Path.Combine(ProfilesDir, safeName.Replace(' ', '_') + ".json");
    }
}
