using System.Text.Json;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class CacheServiceTests : IDisposable
{
    private readonly CacheService _cache = new();
    private readonly List<string> _keys = [];

    private string Key(string name)
    {
        string key = $"ut-cache-{Guid.NewGuid():N}-{name}";
        _keys.Add(key);
        return key;
    }

    public void Dispose()
    {
        foreach (string key in _keys)
        {
            _cache.Invalidate(key);
        }

        _cache.ClearSubdir("ut-subdir");
        _cache.ClearSubdir("ut-clear-a");
        _cache.ClearSubdir("ut-clear-b");
    }

    private sealed record SamplePayload(string Name, int Count, bool Flag);

    [Fact]
    public void SetThenGet_RoundTripsComplexObject()
    {
        string key = Key("roundtrip");
        var payload = new SamplePayload("nettoyage", 42, true);

        _cache.Set(key, payload);
        SamplePayload? readBack = _cache.Get<SamplePayload>(key);

        Assert.NotNull(readBack);
        Assert.Equal(payload, readBack);
    }

    [Fact]
    public void Get_MissingKey_ReturnsDefault()
    {
        string key = Key("absent");

        Assert.Null(_cache.Get<SamplePayload>(key));
        Assert.False(_cache.Has(key));
    }

    [Fact]
    public void Has_TrueAfterSet_FalseAfterInvalidate()
    {
        string key = Key("has");

        _cache.Set(key, "valeur");
        Assert.True(_cache.Has(key));

        _cache.Invalidate(key);
        Assert.False(_cache.Has(key));
        Assert.Null(_cache.Get<string>(key));
    }

    [Fact]
    public void TryGet_ReturnsFalseForExpiredEntry()
    {
        string key = Key("ttl");
        _cache.Set(key, "périmé", TimeSpan.FromMilliseconds(1));

        Thread.Sleep(100);

        Assert.False(_cache.TryGet(key, out string? value));
        Assert.Null(value);
    }

    [Fact]
    public void TryGet_ReturnsTrueForFreshEntry()
    {
        string key = Key("fresh");
        _cache.Set(key, "frais", TimeSpan.FromMinutes(5));

        Assert.True(_cache.TryGet(key, out string? value));
        Assert.Equal("frais", value);
    }

    [Fact]
    public void GetOrSet_CallsFactoryOnceThenServesFromCache()
    {
        string key = Key("factory");
        int calls = 0;

        string first = _cache.GetOrSet(key, () => { calls++; return "généré"; });
        string second = _cache.GetOrSet(key, () => { calls++; return "généré-autre"; });

        Assert.Equal("généré", first);
        Assert.Equal("généré", second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void SubdirVariant_RoundTripsIndependentlyFromRootKey()
    {
        string key = Key("sub");
        string rootKey = Key("root");
        const string subdir = "ut-subdir";

        _cache.Set(rootKey, "racine");
        _cache.SetInSubdir(subdir, key, "sous-dossier");

        Assert.Equal("sous-dossier", _cache.GetFromSubdir<string>(subdir, key));
        Assert.Equal("racine", _cache.Get<string>(rootKey));

        _cache.InvalidateInSubdir(subdir, key);
        Assert.Null(_cache.GetFromSubdir<string>(subdir, key));
        Assert.Equal("racine", _cache.Get<string>(rootKey));
    }

    [Fact]
    public void TryGet_CorruptedEntryIsTreatedAsMissing()
    {
        string key = Key("corrupt");
        string path = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Coclico", "cache", key + ".cache.json");
        _keys.Add(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ ceci n'est pas du json valide");

        Assert.False(_cache.TryGet(key, out string? value));
        Assert.Null(value);
    }

    [Fact]
    public void KeysWithInvalidFileNameCharacters_AreSanitizedConsistently()
    {
        string key = Key("invalid") + ":*?";

        _cache.Set(key, "sanitisé");

        Assert.True(_cache.Has(key));
        Assert.Equal("sanitisé", _cache.Get<string>(key));
    }

    [Fact]
    public async Task SetThenGetAsync_RoundTripsValue()
    {
        string key = Key("async");

        await _cache.SetAsync(key, new SamplePayload("async", 1, false));
        (bool found, SamplePayload? value) = await _cache.TryGetAsync<SamplePayload>(key);

        Assert.True(found);
        Assert.NotNull(value);
        Assert.Equal("async", value.Name);
        Assert.Equal(1, value.Count);
    }

    [Fact]
    public async Task GetOrSetAsync_CallsFactoryOnce()
    {
        string key = Key("factory-async");
        int calls = 0;

        Task<int> Factory()
        {
            calls++;
            return Task.FromResult(7);
        }

        int first = await _cache.GetOrSetAsync(key, Factory);
        int second = await _cache.GetOrSetAsync(key, Factory);

        Assert.Equal(7, first);
        Assert.Equal(7, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ClearSubdir_RemovesOnlyTargetedEntries()
    {
        string subdirA = "ut-clear-a";
        string subdirB = "ut-clear-b";
        string keyA = Key("a");
        string keyB = Key("b");

        _cache.SetInSubdir(subdirA, keyA, "A");
        _cache.SetInSubdir(subdirB, keyB, "B");

        _cache.ClearSubdir(subdirA);

        Assert.Null(_cache.GetFromSubdir<string>(subdirA, keyA));
        Assert.Equal("B", _cache.GetFromSubdir<string>(subdirB, keyB));
    }

    [Fact]
    public void JsonEntries_AreNotIndentedToKeepFilesSmall()
    {
        string key = Key("indent");
        _cache.Set(key, "compact");

        string path = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Coclico", "cache", key + ".cache.json");

        Assert.True(File.Exists(path));
        string json = File.ReadAllText(path);
        Assert.DoesNotContain("\n  ", json);
        Assert.Contains("ExpiresAt", json);
    }
}
