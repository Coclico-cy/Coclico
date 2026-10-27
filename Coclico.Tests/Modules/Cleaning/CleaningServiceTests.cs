using System.Globalization;
using Coclico.Modules.Cleaning.Models;
using Coclico.Modules.Cleaning.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class CleaningServiceTests
{
    [Fact]
    public void FormatSize_EnglishCulture_UsesEnglishUnits()
    {
        using CulturePin pin = CulturePin.For("en-US");

        Assert.Equal("0 MB", CleaningService.FormatSize(0));
        Assert.Equal("0 MB", CleaningService.FormatSize(-100));
        Assert.Equal("1 KB", CleaningService.FormatSize(1024));
        Assert.Equal("0 KB", CleaningService.FormatSize(512));
        Assert.Equal("1.0 MB", CleaningService.FormatSize(1024 * 1024));
        Assert.Equal("1.00 GB", CleaningService.FormatSize(1024L * 1024 * 1024));
    }

    [Fact]
    public void FormatSize_FrenchCulture_UsesFrenchUnits()
    {
        using CulturePin pin = CulturePin.For("fr-FR");

        Assert.Equal("0 Mo", CleaningService.FormatSize(0));
        Assert.Equal("1 Ko", CleaningService.FormatSize(1024));
        Assert.Equal("1,0 Mo", CleaningService.FormatSize(1024 * 1024));
        Assert.Equal("1,00 Go", CleaningService.FormatSize(1024L * 1024 * 1024));
    }

    [Fact]
    public void GetAvailableCategories_ReturnsNonEmptyUniqueKeys()
    {
        List<CleaningCategory> categories = new CleaningService().GetAvailableCategories();

        Assert.NotEmpty(categories);
        Assert.Equal(categories.Count, categories.Select(c => c.Key).Distinct().Count());

        foreach (CleaningCategory c in categories)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Key));
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.True(c.PresetLevel >= 1);
            Assert.True(c.BytesFound >= 0);
        }
    }

    [Fact]
    public void GetAvailableCategories_ContainsCoreCleanupCategories()
    {
        List<string> keys = new CleaningService().GetAvailableCategories().Select(c => c.Key).ToList();

        Assert.Contains("TempFiles", keys);
        Assert.Contains("RecycleBin", keys);
        Assert.Contains("DnsCache", keys);
    }

    private sealed class CulturePin : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        private CulturePin(string culture)
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
        }

        public static CulturePin For(string culture)
        {
            return new CulturePin(culture);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}
