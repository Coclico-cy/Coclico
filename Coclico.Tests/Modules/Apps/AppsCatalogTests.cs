using System.Text.Json;
using Xunit;

namespace Coclico.Tests;

public sealed class AppsCatalogTests
{
    private sealed record CatalogFile(int SchemaVersion, List<CatalogApp> Apps);

    private sealed record CatalogApp(
        string Id,
        string Kind,
        string Name,
        string WingetId,
        string Repo,
        string AssetPattern,
        string Category,
        string Description);

    private static List<CatalogApp> LoadCatalog()
    {
        string path = Path.Combine(
            XamlResourceValidationTests.FindRepoRoot(),
            "Coclico", "Modules", "Apps", "Assets", "apps.json");
        Assert.True(File.Exists(path), $"Catalogue introuvable : {path}");
        CatalogFile? catalog = JsonSerializer.Deserialize<CatalogFile>(
            File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(catalog);
        return catalog!.Apps;
    }

    [Fact]
    public void Catalog_ContainsExactlyTheScriptApps()
    {
        List<CatalogApp> apps = LoadCatalog();
        Assert.Equal(72, apps.Count);
        Assert.Equal(66, apps.Count(a => a.Kind == "winget"));
        Assert.Equal(6, apps.Count(a => a.Kind == "github"));
    }

    [Fact]
    public void Catalog_IdsAndWingetIdsAreUnique()
    {
        List<CatalogApp> apps = LoadCatalog();
        Assert.Equal(apps.Count, apps.Select(a => a.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            apps.Count(a => a.Kind == "winget"),
            apps.Where(a => a.Kind == "winget").Select(a => a.WingetId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Catalog_FieldsAreComplete()
    {
        foreach (CatalogApp app in LoadCatalog())
        {
            Assert.False(string.IsNullOrWhiteSpace(app.Id), $"Id manquant pour {app.Name}");
            Assert.False(string.IsNullOrWhiteSpace(app.Name), $"Nom manquant pour {app.Id}");
            Assert.False(string.IsNullOrWhiteSpace(app.Category), $"Catégorie manquante pour {app.Id}");
            Assert.False(string.IsNullOrWhiteSpace(app.Description), $"Description manquante pour {app.Id}");

            if (app.Kind == "winget")
            {
                Assert.False(string.IsNullOrWhiteSpace(app.WingetId), $"WingetId manquant pour {app.Id}");
            }
            else if (app.Kind == "github")
            {
                Assert.False(string.IsNullOrWhiteSpace(app.Repo), $"Repo manquant pour {app.Id}");
                Assert.False(string.IsNullOrWhiteSpace(app.AssetPattern), $"AssetPattern manquant pour {app.Id}");
                Assert.Matches("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", app.Repo);
            }
            else
            {
                Assert.Fail($"Kind inconnu '{app.Kind}' pour {app.Id}");
            }
        }
    }

    [Fact]
    public void Catalog_DlssSwapperUsesOfficialWingetId()
    {
        List<CatalogApp> apps = LoadCatalog();
        Assert.Contains(apps, a => a.WingetId == "beeradmoore.dlss-swapper");
        Assert.DoesNotContain(apps, a => a.WingetId?.Contains("beeradmore", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void Catalog_GithubAppsMatchScriptRepositories()
    {
        List<CatalogApp> apps = LoadCatalog();
        string[] expectedRepos =
        [
            "hbashton/DS4Windows",
            "Orbmu2k/nvidiaProfileInspector",
            "GSDragoon/RadeonSoftwareSlimmer",
            "MicaForEveryone/MicaForEveryone",
            "sorayuki/obs-multi-rtmp",
            "iandiv/appgroup"
        ];

        foreach (string repo in expectedRepos)
        {
            Assert.Contains(apps, a => a.Kind == "github" && a.Repo == repo);
        }
    }

    private sealed record PacksFile(int SchemaVersion, List<PackDefinition> Packs);

    private sealed record PackDefinition(string Id, string Name, string Description, List<string> AppIds);

    private static List<PackDefinition> LoadPacks()
    {
        string path = Path.Combine(
            XamlResourceValidationTests.FindRepoRoot(),
            "Coclico", "Modules", "Apps", "Assets", "apppacks.json");
        Assert.True(File.Exists(path), $"Fichier packs introuvable : {path}");
        PacksFile? packs = JsonSerializer.Deserialize<PacksFile>(
            File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(packs);
        return packs!.Packs;
    }

    [Fact]
    public void AppPacks_HaveUniqueIdsAndCompleteFields()
    {
        List<PackDefinition> packs = LoadPacks();
        Assert.Equal(16, packs.Count);
        Assert.Equal(packs.Count, packs.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (PackDefinition pack in packs)
        {
            Assert.False(string.IsNullOrWhiteSpace(pack.Name), $"Nom manquant pour {pack.Id}");
            Assert.False(string.IsNullOrWhiteSpace(pack.Description), $"Description manquante pour {pack.Id}");
            Assert.NotEmpty(pack.AppIds);
        }
    }

    [Fact]
    public void AppPacks_OnlyReferenceCatalogApps()
    {
        List<CatalogApp> apps = LoadCatalog();
        HashSet<string> catalogIds = apps.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (PackDefinition pack in LoadPacks())
        {
            foreach (string appId in pack.AppIds)
            {
                Assert.True(catalogIds.Contains(appId), $"App '{appId}' du pack {pack.Id} absente du catalogue");
            }
        }
    }

    [Fact]
    public void AppPacks_EachAppBelongsToExactlyOnePack()
    {
        List<CatalogApp> apps = LoadCatalog();
        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (PackDefinition pack in LoadPacks())
        {
            foreach (string appId in pack.AppIds)
            {
                occurrences[appId] = occurrences.GetValueOrDefault(appId) + 1;
            }
        }

        Assert.Equal(apps.Count, occurrences.Count);
        foreach (CatalogApp app in apps)
        {
            Assert.True(occurrences.GetValueOrDefault(app.Id) == 1, $"L'app {app.Id} doit appartenir à exactement un pack");
        }
    }

    [Fact]
    public void Catalog_ControllerDriversUseOfficialWingetIds()
    {
        List<CatalogApp> apps = LoadCatalog();
        Assert.Contains(apps, a => a.WingetId == "ViGEm.ViGEmBus");
        Assert.Contains(apps, a => a.WingetId == "Nefarius.HidHide");
    }
}
