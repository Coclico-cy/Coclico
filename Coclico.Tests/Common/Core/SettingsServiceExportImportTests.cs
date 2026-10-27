using Coclico.Services;
using Xunit;

namespace Coclico.Tests.Common.Core;

public sealed class SettingsServiceExportImportTests : IDisposable
{
    private readonly string _tempDir = Path.Join(Path.GetTempPath(), "coclico-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsServiceExportImportTests()
    {
        _ = Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            return;
        }
    }

    [Fact]
    public async Task ExportToAsync_WritesValidJsonFile()
    {
        var service = new SettingsService();
        service.Settings.Language = "en";
        string path = Path.Join(_tempDir, "export.json");

        bool success = await service.ExportToAsync(path);

        Assert.True(success);
        Assert.True(File.Exists(path));
        string json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"language\"", json);
        Assert.Contains("\"en\"", json);
    }

    [Fact]
    public async Task ExportToAsync_EmptyPath_ReturnsFalse()
    {
        var service = new SettingsService();

        bool success = await service.ExportToAsync(" ");

        Assert.False(success);
    }

    [Fact]
    public async Task ImportFromAsync_MissingFile_ReturnsFalse()
    {
        var service = new SettingsService();

        bool success = await service.ImportFromAsync(Path.Join(_tempDir, "inexistant.json"));

        Assert.False(success);
    }

    [Fact]
    public async Task ImportFromAsync_RoundTrip_AppliesExportedSettings()
    {
        var exporter = new SettingsService();
        exporter.Settings.Language = "en";
        string path = Path.Join(_tempDir, "roundtrip.json");
        Assert.True(await exporter.ExportToAsync(path));

        var importer = new SettingsService();
        Assert.NotEqual("en", importer.Settings.Language);

        bool success = await importer.ImportFromAsync(path);

        Assert.True(success);
        Assert.Equal("en", importer.Settings.Language);
    }

    [Fact]
    public async Task ImportFromAsync_PreservesInstallationFields()
    {
        string path = Path.Join(_tempDir, "installation-fields.json");
        await File.WriteAllTextAsync(path, "{ \"firstRun\": true, \"hasSeenWelcome\": true, \"language\": \"en\" }");

        var importer = new SettingsService();
        importer.Settings.FirstRun = false;
        importer.Settings.HasSeenWelcome = false;

        bool success = await importer.ImportFromAsync(path);

        Assert.True(success);
        Assert.False(importer.Settings.FirstRun);
        Assert.False(importer.Settings.HasSeenWelcome);
        Assert.Equal("en", importer.Settings.Language);
    }

    [Fact]
    public async Task ImportFromAsync_InvalidJson_ReturnsFalse()
    {
        string path = Path.Join(_tempDir, "invalide.json");
        await File.WriteAllTextAsync(path, "ceci n'est pas du json");

        var service = new SettingsService();

        bool success = await service.ImportFromAsync(path);

        Assert.False(success);
    }

    [Fact]
    public async Task ImportFromAsync_UnknownLanguage_IsSanitized()
    {
        string path = Path.Join(_tempDir, "langue-invalide.json");
        await File.WriteAllTextAsync(path, "{ \"language\": \"zz\" }");

        var service = new SettingsService();

        bool success = await service.ImportFromAsync(path);

        Assert.True(success);
        Assert.Equal("fr", service.Settings.Language);
    }
}
