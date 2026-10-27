using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Coclico.Modules.Apps.Services;

public sealed class AppCatalogService
{
    public sealed record AppCatalogEntry(
        string Id,
        string Kind,
        string Name,
        string WingetId,
        string Repo,
        string AssetPattern,
        string Category,
        string Description);

    public sealed record AppPackDefinition(string Id, string Name, string Description, List<string> AppIds);

    private sealed record CatalogFile(int SchemaVersion, List<AppCatalogEntry> Apps);
    private sealed record PacksFile(int SchemaVersion, List<AppPackDefinition> Packs);

    private const string ResourceName = "Coclico.apps.json";
    private const string PacksResourceName = "Coclico.apppacks.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private List<AppCatalogEntry>? _cached;
    private List<AppPackDefinition>? _cachedPacks;

    public List<AppPackDefinition> GetPacks()
    {
        if (_cachedPacks != null)
        {
            return _cachedPacks;
        }

        Assembly assembly = typeof(AppCatalogService).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(PacksResourceName)
            ?? throw new InvalidOperationException($"Ressource embarquée introuvable : {PacksResourceName}");
        PacksFile packsFile = JsonSerializer.Deserialize<PacksFile>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Catalogue apppacks.json invalide.");
        _cachedPacks = [.. packsFile.Packs];
        return _cachedPacks;
    }

    public AppCatalogEntry? FindApp(string appId)
    {
        return GetApps().FirstOrDefault(a => string.Equals(a.Id, appId, StringComparison.OrdinalIgnoreCase));
    }

    public List<AppCatalogEntry> GetApps()
    {
        if (_cached != null)
        {
            return _cached;
        }

        Assembly assembly = typeof(AppCatalogService).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Ressource embarquée introuvable : {ResourceName}");
        CatalogFile catalog = JsonSerializer.Deserialize<CatalogFile>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Catalogue apps.json invalide.");
        _cached = [.. catalog.Apps];
        return _cached;
    }

    public List<string> GetCategories()
    {
        return [.. GetApps().Select(static a => a.Category).Distinct().OrderBy(static c => c, StringComparer.OrdinalIgnoreCase)];
    }

    public static string GetSafeFolderName(string name)
    {
        string cleaned = string.Concat(name.Where(static c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '.' or '-'));
        return string.IsNullOrWhiteSpace(cleaned) ? "App" : cleaned.Trim();
    }
}
