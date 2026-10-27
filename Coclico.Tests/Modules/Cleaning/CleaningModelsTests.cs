using Coclico.Modules.Cleaning.Models;
using Xunit;

namespace Coclico.Tests;

public sealed class CleaningModelsTests
{
    [Fact]
    public void CleaningCategory_HasSensibleDefaults()
    {
        var category = new CleaningCategory();

        Assert.Equal(string.Empty, category.Key);
        Assert.Equal(string.Empty, category.Name);
        Assert.Equal("🗑️", category.Icon);
        Assert.Equal(2, category.PresetLevel);
        Assert.True(category.IsSelected);
        Assert.True(category.IsSizeMeasurable);
        Assert.Equal(0, category.BytesFound);
        Assert.Equal(0, category.FileCount);
        Assert.Equal(0, category.BytesFreed);
        Assert.Equal(0, category.FilesDeleted);
    }

    [Fact]
    public void CleaningResult_StartsEmpty()
    {
        var result = new CleaningResult();

        Assert.Equal(0, result.TotalBytesFreed);
        Assert.Equal(0, result.FilesDeleted);
        Assert.Equal(0, result.DirectoriesCleaned);
        Assert.NotNull(result.Categories);
        Assert.Empty(result.Categories);
        Assert.NotNull(result.Errors);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CleaningCategory_PropertiesAreWritable()
    {
        var category = new CleaningCategory
        {
            Key = "temp",
            Name = "Fichiers temporaires",
            TargetPath = @"C:\Windows\Temp",
            PresetLevel = 1,
            BytesFound = 123_456,
            FileCount = 42,
            BytesFreed = 100_000,
            FilesDeleted = 40,
            IsSelected = false,
            IsSizeMeasurable = false
        };

        Assert.Equal("temp", category.Key);
        Assert.Equal("Fichiers temporaires", category.Name);
        Assert.Equal(@"C:\Windows\Temp", category.TargetPath);
        Assert.Equal(1, category.PresetLevel);
        Assert.Equal(123_456, category.BytesFound);
        Assert.Equal(42, category.FileCount);
        Assert.Equal(100_000, category.BytesFreed);
        Assert.Equal(40, category.FilesDeleted);
        Assert.False(category.IsSelected);
        Assert.False(category.IsSizeMeasurable);
    }

    [Fact]
    public void CleaningResult_AggregatesCategories()
    {
        var result = new CleaningResult
        {
            Categories =
            [
                new CleaningCategory { BytesFreed = 100, FilesDeleted = 2 },
                new CleaningCategory { BytesFreed = 50, FilesDeleted = 1 }
            ],
            TotalBytesFreed = 150,
            FilesDeleted = 3
        };

        Assert.Equal(2, result.Categories.Count);
        Assert.Equal(150, result.TotalBytesFreed);
        Assert.Equal(3, result.FilesDeleted);
    }
}
