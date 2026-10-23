using System.IO;
using Coclico.Models.Network;
using Coclico.Services.Network;
using Xunit;

namespace Coclico.Tests;

public sealed class NetworkTuningCatalogTests
{
    [Fact]
    public void CatalogContainsExactlyNineParameters()
    {
        Assert.True(NetworkTuningCatalog.Definitions.Count == 9,
            $"Attendu : 9 paramètres, trouvé : {NetworkTuningCatalog.Definitions.Count}");
    }

    [Fact]
    public void AllParameterIdsAreUnique()
    {
        var ids = NetworkTuningCatalog.Definitions.Select(p => p.Id).ToList();
        Assert.True(ids.Count == ids.Distinct().Count(), "Des identifiants de paramètres sont dupliqués.");
    }

    [Fact]
    public void TcpParametersUseOfficialNetshTargets()
    {
        Assert.Equal("normal", NetworkTuningCatalog.Find(NetworkTuningCatalog.AutoTuningId)?.TargetValue);
        Assert.Equal("disabled", NetworkTuningCatalog.Find(NetworkTuningCatalog.ScalingHeuristicsId)?.TargetValue);
        Assert.Equal("disabled", NetworkTuningCatalog.Find(NetworkTuningCatalog.EcnCapabilityId)?.TargetValue);
        Assert.Equal("disabled", NetworkTuningCatalog.Find(NetworkTuningCatalog.TimestampsId)?.TargetValue);
        Assert.Equal("enabled", NetworkTuningCatalog.Find(NetworkTuningCatalog.RssId)?.TargetValue);
    }

    [Fact]
    public void AdapterParametersDisablePowerSaving()
    {
        Assert.Equal("Disabled", NetworkTuningCatalog.Find(NetworkTuningCatalog.AdapterPowerId)?.TargetValue);
        Assert.Equal("0", NetworkTuningCatalog.Find(NetworkTuningCatalog.AdapterEeeId)?.TargetValue);
        Assert.Equal("0", NetworkTuningCatalog.Find(NetworkTuningCatalog.AdapterFlowControlId)?.TargetValue);
        Assert.Equal("0", NetworkTuningCatalog.Find(NetworkTuningCatalog.AdapterInterruptModerationId)?.TargetValue);
    }

    [Fact]
    public void CatalogContainsNoRegistryHacks()
    {
        Assert.All(NetworkTuningCatalog.Definitions, p =>
            Assert.True(p.Category is NetworkTuningCategory.TcpStack or NetworkTuningCategory.Adapter,
                $"Catégorie inattendue pour {p.Id} : {p.Category}"));
        Assert.DoesNotContain(NetworkTuningCatalog.Definitions,
            p => p.Id.Contains("throttling", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(NetworkTuningCatalog.Definitions,
            p => p.Id.Contains("responsiveness", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(NetworkTuningCatalog.Definitions,
            p => p.Id.Contains("ackfrequency", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(NetworkTuningCatalog.Definitions,
            p => p.Id.Contains("nodelay", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CategoriesCoverTcpAndAdapter()
    {
        var categories = NetworkTuningCatalog.Definitions.Select(p => p.Category).Distinct().ToList();
        Assert.Contains(NetworkTuningCategory.TcpStack, categories);
        Assert.Contains(NetworkTuningCategory.Adapter, categories);
        Assert.True(categories.Count == 2, "Le catalogue doit couvrir exactement 2 catégories.");
    }

    [Fact]
    public void EveryParameterDeclaresLocalizationKeys()
    {
        foreach (var parameter in NetworkTuningCatalog.Definitions)
        {
            Assert.True(parameter.NameKey.StartsWith("Network_Param", StringComparison.Ordinal),
                $"Clé de nom invalide pour {parameter.Id} : {parameter.NameKey}");
            Assert.True(parameter.DescriptionKey.StartsWith("Network_Param", StringComparison.Ordinal),
                $"Clé de description invalide pour {parameter.Id} : {parameter.DescriptionKey}");
        }
    }

    [Fact]
    public void CatalogIdsAreStable()
    {
        var expected = new[]
        {
            NetworkTuningCatalog.AutoTuningId,
            NetworkTuningCatalog.ScalingHeuristicsId,
            NetworkTuningCatalog.EcnCapabilityId,
            NetworkTuningCatalog.TimestampsId,
            NetworkTuningCatalog.RssId,
            NetworkTuningCatalog.AdapterPowerId,
            NetworkTuningCatalog.AdapterEeeId,
            NetworkTuningCatalog.AdapterFlowControlId,
            NetworkTuningCatalog.AdapterInterruptModerationId
        };
        var ids = NetworkTuningCatalog.Definitions.Select(p => p.Id).ToHashSet();
        foreach (var id in expected)
        {
            Assert.True(ids.Contains(id), $"Identifiant manquant au catalogue : {id}");
        }
    }

    [Fact]
    public void CatalogLocalizationKeysExistInFrenchDictionary()
    {
        string repoRoot = XamlResourceValidationTests.FindRepoRoot();
        string frContent = File.ReadAllText(Path.Combine(repoRoot, "Coclico", "Resources", "Lang", "fr.xaml"));
        foreach (var parameter in NetworkTuningCatalog.Definitions)
        {
            Assert.True(frContent.Contains($"x:Key=\"{parameter.NameKey}\"", StringComparison.Ordinal),
                $"Clé i18n absente de fr.xaml : {parameter.NameKey}");
            Assert.True(frContent.Contains($"x:Key=\"{parameter.DescriptionKey}\"", StringComparison.Ordinal),
                $"Clé i18n absente de fr.xaml : {parameter.DescriptionKey}");
        }
    }
}
