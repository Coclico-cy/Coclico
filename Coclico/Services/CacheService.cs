#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Coclico.Services;

public class CacheService : ICacheService
{
    private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico", "cache");

    private static readonly JsonSerializerOptions _serializeOptions = new() { WriteIndented = false };

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new(StringComparer.Ordinal);

    private SemaphoreSlim GetLock(string path) =>
        _keyLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));

    public CacheService()
    {
        Directory.CreateDirectory(CacheDir);
    }

    public void Set<T>(string key, T value, TimeSpan? ttl = null)
    {
        try
        {
            var entry = new CacheEntry<T>
            {
                Value = value,
                ExpiresAt = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : DateTime.MaxValue,
                CreatedAt = DateTime.UtcNow
            };
            using var stream = File.Create(GetPath(key));
            JsonSerializer.Serialize(stream, entry, _serializeOptions);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CacheService.Set");
        }
    }

    public T? Get<T>(string key)
    {
        try
        {
            var path = GetPath(key);
            if (!File.Exists(path)) return default;

            using var stream = File.OpenRead(path);
            var entry = JsonSerializer.Deserialize<CacheEntry<T>>(stream, _serializeOptions);
            if (entry == null) return default;

            if (DateTime.UtcNow > entry.ExpiresAt)
            {
                File.Delete(path);
                return default;
            }
            return entry.Value;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CacheService.Get");
            return default;
        }
    }

    public bool Has(string key)
    {
        try
        {
            var path = GetPath(key);
            if (!File.Exists(path)) return false;

            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("ExpiresAt", out var expiryEl))
            {
                if (DateTime.TryParse(expiryEl.GetString(), out var expiry))
                    return DateTime.UtcNow <= expiry;
            }
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CacheService.Has");
            return false;
        }
    }

    public void Set<T>(string subdir, string key, T value, TimeSpan? ttl = null)
    {
        try
        {
            Directory.CreateDirectory(GetSubdirPath(subdir));
            var entry = new CacheEntry<T>
            {
                Value = value,
                ExpiresAt = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : DateTime.MaxValue,
                CreatedAt = DateTime.UtcNow
            };
            using var stream = File.Create(GetPath(subdir, key));
            JsonSerializer.Serialize(stream, entry, _serializeOptions);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CacheService.Set(subdir)");
        }
    }

    public T? Get<T>(string subdir, string key)
    {
        try
        {
            var path = GetPath(subdir, key);
            if (!File.Exists(path)) return default;
            using var stream = File.OpenRead(path);
            var entry = JsonSerializer.Deserialize<CacheEntry<T>>(stream, _serializeOptions);
            if (entry == null) return default;
            if (DateTime.UtcNow > entry.ExpiresAt) { File.Delete(path); return default; }
            return entry.Value;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CacheService.Get(subdir)");
            return default;
        }
    }

    public bool Has(string subdir, string key)
    {
        try
        {
            var path = GetPath(subdir, key);
            if (!File.Exists(path)) return false;
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("ExpiresAt", out var expiryEl))
            {
                if (DateTime.TryParse(expiryEl.GetString(), out var expiry))
                    return DateTime.UtcNow <= expiry;
            }
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CacheService.Has(subdir)");
            return false;
        }
    }

    public void Invalidate(string subdir, string key)
    {
        try { File.Delete(GetPath(subdir, key)); }
        catch (Exception ex) { LoggingService.LogException(ex, "CacheService.Invalidate(subdir)"); }
    }

    public void ClearSubdir(string subdir)
    {
        try
        {
            var dir = GetSubdirPath(subdir);
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*.json"))
                File.Delete(f);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CacheService.ClearSubdir");
        }
    }

    public void Invalidate(string key)
    {
        try { File.Delete(GetPath(key)); }
        catch (Exception ex) { LoggingService.LogException(ex, "CacheService.Invalidate(key)"); }
    }

    public void Clear()
    {
        try
        {
            foreach (var f in Directory.GetFiles(CacheDir, "*.json", SearchOption.AllDirectories))
                File.Delete(f);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CacheService.Clear");
        }
    }

    public long GetCacheSizeBytes()
    {
        try
        {
            long total = 0;
            foreach (var f in Directory.GetFiles(CacheDir, "*.json", SearchOption.AllDirectories))
                total += new FileInfo(f).Length;
            return total;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CacheService.GetCacheSizeBytes");
            return 0;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null)
    {
        var path = GetPath(key);
        var sem = GetLock(path);
        await sem.WaitAsync().ConfigureAwait(false);
        try
        {
            var entry = new CacheEntry<T>
            {
                Value = value,
                ExpiresAt = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : DateTime.MaxValue,
                CreatedAt = DateTime.UtcNow
            };
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, entry, _serializeOptions).ConfigureAwait(false);
        }
        catch (Exception ex) { LoggingService.LogException(ex, "CacheService.SetAsync"); }
        finally { sem.Release(); }
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var path = GetPath(key);
        var sem = GetLock(path);
        await sem.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return default;
            await using var stream = File.OpenRead(path);
            var entry = await JsonSerializer.DeserializeAsync<CacheEntry<T>>(stream, _serializeOptions).ConfigureAwait(false);
            if (entry == null) return default;
            if (DateTime.UtcNow > entry.ExpiresAt) { File.Delete(path); return default; }
            return entry.Value;
        }
        catch (Exception ex) { LoggingService.LogException(ex, "CacheService.GetAsync"); return default; }
        finally { sem.Release(); }
    }

    public async Task<bool> HasAsync(string key)
    {
        var path = GetPath(key);
        var sem = GetLock(path);
        await sem.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return false;
            await using var stream = File.OpenRead(path);
            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("ExpiresAt", out var expiryEl))
            {
                if (DateTime.TryParse(expiryEl.GetString(), out var expiry))
                    return DateTime.UtcNow <= expiry;
            }
            return true;
        }
        catch (Exception ex) { LoggingService.LogException(ex, "CacheService.HasAsync"); return false; }
        finally { sem.Release(); }
    }

    public async Task InvalidateAsync(string key)
    {
        var path = GetPath(key);
        var sem = GetLock(path);
        await sem.WaitAsync().ConfigureAwait(false);
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { LoggingService.LogException(ex, "CacheService.InvalidateAsync"); }
        finally { sem.Release(); }
    }

    public async Task ClearAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                foreach (var f in Directory.GetFiles(CacheDir, "*.json", SearchOption.AllDirectories))
                    File.Delete(f);
            }).ConfigureAwait(false);
        }
        catch (Exception ex) { LoggingService.LogException(ex, "CacheService.ClearAsync"); }
    }

    private string GetSubdirPath(string subdir) =>
        Path.Combine(CacheDir, subdir.Replace('/', '_').Replace('\\', '_'));

    private string GetPath(string subdir, string key) =>
        Path.Combine(GetSubdirPath(subdir), key.Replace('/', '_').Replace('\\', '_') + ".cache.json");

    private string GetPath(string key) =>
        Path.Combine(CacheDir, key.Replace('/', '_').Replace('\\', '_') + ".cache.json");

    private class CacheEntry<T>
    {
        public T? Value { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
