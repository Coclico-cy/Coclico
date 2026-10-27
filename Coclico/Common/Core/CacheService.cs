using System.IO;
using System.Linq;
using System.Text.Json;

namespace Coclico.Services;

public sealed class CacheService : ICacheService
{
    private static readonly string CacheDir = Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico", "cache");

    private const int LockStripeCount = 32;
    private static readonly SemaphoreSlim[] _stripedLocks =
        Enumerable.Range(0, LockStripeCount).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static readonly char[] _invalidChars = Path.GetInvalidFileNameChars();
    private static readonly JsonSerializerOptions _serializeOptions = new() { WriteIndented = false };

    public CacheService()
    {
        _ = Directory.CreateDirectory(CacheDir);
    }

    public void Set<T>(string key, T value, TimeSpan? ttl = null)
    {
        string path = GetPath(key);
        SemaphoreSlim sem = GetLock(path);
        sem.Wait();
        try
        {
            WriteEntry(path, value, ttl);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "CacheService.Set");
        }
        finally
        {
            _ = sem.Release();
        }
    }

    public T? Get<T>(string key)
    {
        return TryGet(key, out T? value) ? value : default;
    }

    public bool TryGet<T>(string key, out T? value)
    {
        string path = GetPath(key);
        SemaphoreSlim sem = GetLock(path);
        sem.Wait();
        try
        {
            return TryReadEntry(path, out value);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "CacheService.TryGet");
            value = default;
            return false;
        }
        finally
        {
            _ = sem.Release();
        }
    }

    public bool Has(string key)
    {
        string path = GetPath(key);
        SemaphoreSlim sem = GetLock(path);
        sem.Wait();
        try
        {
            return HasValidExpiry(path);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "CacheService.Has");
            return false;
        }
        finally
        {
            _ = sem.Release();
        }
    }

    public void Invalidate(string key)
    {
        string path = GetPath(key);
        SemaphoreSlim sem = GetLock(path);
        sem.Wait();
        try
        {
            DeleteBestEffort(path);
        }
        finally
        {
            _ = sem.Release();
        }
    }

    public void SetInSubdir<T>(string subdir, string key, T value, TimeSpan? ttl = null)
    {
        string path = GetPath(subdir, key);
        SemaphoreSlim sem = GetLock(path);
        sem.Wait();
        try
        {
            _ = Directory.CreateDirectory(GetSubdirPath(subdir));
            WriteEntry(path, value, ttl);
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            LoggingService.LogException(ex, "CacheService.Set(subdir)");
        }
        finally
        {
            _ = sem.Release();
        }
    }

    public T? GetFromSubdir<T>(string subdir, string key)
    {
        string path = GetPath(subdir, key);
        SemaphoreSlim sem = GetLock(path);
        sem.Wait();
        try
        {
            return TryReadEntry(path, out T? value) ? value : default;
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "CacheService.Get(subdir)");
            return default;
        }
        finally
        {
            _ = sem.Release();
        }
    }

    public bool HasInSubdir(string subdir, string key)
    {
        string path = GetPath(subdir, key);
        SemaphoreSlim sem = GetLock(path);
        sem.Wait();
        try
        {
            return HasValidExpiry(path);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "CacheService.Has(subdir)");
            return false;
        }
        finally
        {
            _ = sem.Release();
        }
    }

    public void InvalidateInSubdir(string subdir, string key)
    {
        string path = GetPath(subdir, key);
        SemaphoreSlim sem = GetLock(path);
        sem.Wait();
        try
        {
            DeleteBestEffort(path);
        }
        finally
        {
            _ = sem.Release();
        }
    }

    public void ClearSubdir(string subdir)
    {
        try
        {
            string dir = GetSubdirPath(subdir);
            if (Directory.Exists(dir))
            {
                DeleteAllJsonFiles(dir, allDirectories: false);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            LoggingService.LogException(ex, "CacheService.ClearSubdir");
        }
    }

    public void Clear()
    {
        try
        {
            DeleteAllJsonFiles(CacheDir, allDirectories: true);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "CacheService.Clear");
        }
    }

    public long GetCacheSizeBytes()
    {
        try
        {
            long total = 0;
            foreach (string file in Directory.EnumerateFiles(CacheDir, "*.json", SearchOption.AllDirectories))
            {
                total += new FileInfo(file).Length;
            }

            return total;
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            LoggingService.LogException(ex, "CacheService.GetCacheSizeBytes");
            return 0;
        }
    }

    public T GetOrSet<T>(string key, Func<T> factory, TimeSpan? ttl = null)
    {
        string path = GetPath(key);
        if (TryReadEntry(path, out T? cached))
        {
            return cached!;
        }

        T value = factory();
        Set(key, value, ttl);
        return value;
    }

    public T GetOrSetInSubdir<T>(string subdir, string key, Func<T> factory, TimeSpan? ttl = null)
    {
        string path = GetPath(subdir, key);
        if (TryReadEntry(path, out T? cached))
        {
            return cached!;
        }

        T value = factory();
        SetInSubdir(subdir, key, value, ttl);
        return value;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null)
    {
        string path = GetPath(key);
        SemaphoreSlim sem = GetLock(path);
        await sem.WaitAsync().ConfigureAwait(false);
        try
        {
            await WriteEntryAsync(path, value, ttl).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "CacheService.SetAsync");
        }
        finally
        {
            _ = sem.Release();
        }
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        (bool found, T? value) = await TryGetAsync<T>(key).ConfigureAwait(false);
        return found ? value : default;
    }

    public async Task<(bool Found, T? Value)> TryGetAsync<T>(string key)
    {
        string path = GetPath(key);
        SemaphoreSlim sem = GetLock(path);
        await sem.WaitAsync().ConfigureAwait(false);
        try
        {
            return await TryReadEntryAsync<T>(path).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "CacheService.TryGetAsync");
            return (false, default);
        }
        finally
        {
            _ = sem.Release();
        }
    }

    public async Task<bool> HasAsync(string key)
    {
        string path = GetPath(key);
        SemaphoreSlim sem = GetLock(path);
        await sem.WaitAsync().ConfigureAwait(false);
        try
        {
            return await HasValidExpiryAsync(path).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "CacheService.HasAsync");
            return false;
        }
        finally
        {
            _ = sem.Release();
        }
    }

    public async Task InvalidateAsync(string key)
    {
        string path = GetPath(key);
        SemaphoreSlim sem = GetLock(path);
        await sem.WaitAsync().ConfigureAwait(false);
        try
        {
            DeleteBestEffort(path);
        }
        finally
        {
            _ = sem.Release();
        }
    }

    public Task ClearAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                DeleteAllJsonFiles(CacheDir, allDirectories: true);
            }
            catch (Exception ex) when (ex is not System.OutOfMemoryException)
            {
                LoggingService.LogException(ex, "CacheService.ClearAsync");
            }
        });
    }

    public async Task SetInSubdirAsync<T>(string subdir, string key, T value, TimeSpan? ttl = null)
    {
        string path = GetPath(subdir, key);
        SemaphoreSlim sem = GetLock(path);
        await sem.WaitAsync().ConfigureAwait(false);
        try
        {
            _ = Directory.CreateDirectory(GetSubdirPath(subdir));
            await WriteEntryAsync(path, value, ttl).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            LoggingService.LogException(ex, "CacheService.SetAsync(subdir)");
        }
        finally
        {
            _ = sem.Release();
        }
    }

    public async Task<T?> GetFromSubdirAsync<T>(string subdir, string key)
    {
        string path = GetPath(subdir, key);
        SemaphoreSlim sem = GetLock(path);
        await sem.WaitAsync().ConfigureAwait(false);
        try
        {
            (bool found, T? value) = await TryReadEntryAsync<T>(path).ConfigureAwait(false);
            return found ? value : default;
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "CacheService.GetAsync(subdir)");
            return default;
        }
        finally
        {
            _ = sem.Release();
        }
    }

    public async Task InvalidateInSubdirAsync(string subdir, string key)
    {
        string path = GetPath(subdir, key);
        SemaphoreSlim sem = GetLock(path);
        await sem.WaitAsync().ConfigureAwait(false);
        try
        {
            DeleteBestEffort(path);
        }
        finally
        {
            _ = sem.Release();
        }
    }

    public Task ClearSubdirAsync(string subdir)
    {
        return Task.Run(() =>
        {
            try
            {
                string dir = GetSubdirPath(subdir);
                if (Directory.Exists(dir))
                {
                    DeleteAllJsonFiles(dir, allDirectories: false);
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
            {
                LoggingService.LogException(ex, "CacheService.ClearSubdirAsync");
            }
        });
    }

    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null)
    {
        string path = GetPath(key);
        (bool found, T? cached) = await TryReadEntryAsync<T>(path).ConfigureAwait(false);
        if (found)
        {
            return cached!;
        }

        T value = await factory().ConfigureAwait(false);
        await SetAsync(key, value, ttl).ConfigureAwait(false);
        return value;
    }

    public async Task<T> GetOrSetInSubdirAsync<T>(string subdir, string key, Func<Task<T>> factory, TimeSpan? ttl = null)
    {
        string path = GetPath(subdir, key);
        (bool found, T? cached) = await TryReadEntryAsync<T>(path).ConfigureAwait(false);
        if (found)
        {
            return cached!;
        }

        T value = await factory().ConfigureAwait(false);
        await SetInSubdirAsync(subdir, key, value, ttl).ConfigureAwait(false);
        return value;
    }

    private static bool TryReadEntry<T>(string path, out T? value)
    {
        value = default;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            CacheEntry<T>? entry = JsonSerializer.Deserialize<CacheEntry<T>>(stream, _serializeOptions);
            return EntryIsValid(entry, path, out value);
        }
        catch (JsonException ex)
        {
            LoggingService.LogException(ex, "CacheService.CorruptedEntry");
            DeleteBestEffort(path);
            return false;
        }
    }

    private static async Task<(bool Found, T? Value)> TryReadEntryAsync<T>(string path)
    {
        if (!File.Exists(path))
        {
            return (false, default);
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            CacheEntry<T>? entry = await JsonSerializer
                .DeserializeAsync<CacheEntry<T>>(stream, _serializeOptions)
                .ConfigureAwait(false);

            return EntryIsValid(entry, path, out T? value)
                ? (true, value)
                : (false, default);
        }
        catch (JsonException ex)
        {
            LoggingService.LogException(ex, "CacheService.CorruptedEntry");
            DeleteBestEffort(path);
            return (false, default);
        }
    }

    private static bool EntryIsValid<T>(CacheEntry<T>? entry, string path, out T? value)
    {
        if (entry == null)
        {
            value = default;
            return false;
        }

        if (DateTime.UtcNow > entry.ExpiresAt)
        {
            RecordCacheOutcome(path, false);
            DeleteBestEffort(path);
            value = default;
            return false;
        }

        RecordCacheOutcome(path, true);
        value = entry.Value;
        return true;
    }

    private static void RecordCacheOutcome(string key, bool wasFresh)
    {
        try
        {
            CentralUnitStigmergy.Current.RecordCacheOutcome(key, wasFresh);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "CacheService.RecordCacheOutcome");
        }
    }

    private static bool HasValidExpiry(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        using FileStream stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.TryGetProperty("ExpiresAt", out JsonElement expiryEl)
            && expiryEl.ValueKind == JsonValueKind.String
            && DateTime.TryParse(expiryEl.GetString(), out DateTime expiry))
        {
            return DateTime.UtcNow <= expiry;
        }

        return true;
    }

    private static async Task<bool> HasValidExpiryAsync(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        await using FileStream stream = File.OpenRead(path);
        using JsonDocument doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        if (doc.RootElement.TryGetProperty("ExpiresAt", out JsonElement expiryEl)
            && expiryEl.ValueKind == JsonValueKind.String
            && DateTime.TryParse(expiryEl.GetString(), out DateTime expiry))
        {
            return DateTime.UtcNow <= expiry;
        }

        return true;
    }

    private static void WriteEntry<T>(string path, T value, TimeSpan? ttl)
    {
        var entry = new CacheEntry<T>
        {
            Value = value,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = BuildExpiry(path, ttl)
        };

        WriteAtomically(path, stream => JsonSerializer.Serialize(stream, entry, _serializeOptions));
    }

    private static async Task WriteEntryAsync<T>(string path, T value, TimeSpan? ttl)
    {
        var entry = new CacheEntry<T>
        {
            Value = value,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = BuildExpiry(path, ttl)
        };

        await WriteAtomicallyAsync(path, stream => JsonSerializer.SerializeAsync(stream, entry, _serializeOptions))
            .ConfigureAwait(false);
    }

    private static DateTime BuildExpiry(string key, TimeSpan? ttl)
    {
        return ttl.HasValue ? DateTime.UtcNow.Add(AdaptTtl(key, ttl.Value)) : DateTime.MaxValue;
    }

    private static TimeSpan AdaptTtl(string key, TimeSpan baseTtl)
    {
        try
        {
            return CentralUnitStigmergy.Current.GetAdaptedTtl(key, baseTtl);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "CacheService.AdaptTtl");
            return baseTtl;
        }
    }

    private static void WriteAtomically(string path, Action<FileStream> write)
    {
        string tmpPath = path + ".tmp";
        try
        {
            using (FileStream stream = File.Create(tmpPath))
            {
                write(stream);
            }

            File.Move(tmpPath, path, overwrite: true);
        }
        catch
        {
            DeleteBestEffort(tmpPath);
            throw;
        }
    }

    private static async Task WriteAtomicallyAsync(string path, Func<FileStream, Task> write)
    {
        string tmpPath = path + ".tmp";
        try
        {
            await using (FileStream stream = File.Create(tmpPath))
            {
                await write(stream).ConfigureAwait(false);
            }

            File.Move(tmpPath, path, overwrite: true);
        }
        catch
        {
            DeleteBestEffort(tmpPath);
            throw;
        }
    }

    private static void DeleteBestEffort(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            LoggingService.LogWarning($"[CacheService] Fichier cache occupé, suppression ignorée : {ex.Message}");
        }
    }

    private static void DeleteAllJsonFiles(string dir, bool allDirectories)
    {
        SearchOption option = allDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (string file in Directory.EnumerateFiles(dir, "*.json", option))
        {
            SemaphoreSlim sem = GetLock(file);
            sem.Wait();
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
            {
                LoggingService.LogException(ex, "CacheService.DeleteAllJsonFiles");
            }
            finally
            {
                _ = sem.Release();
            }
        }
    }

    private static SemaphoreSlim GetLock(string key)
    {
        return _stripedLocks[(uint)key.GetHashCode() % LockStripeCount];
    }

    private static string SanitizeKey(string key)
    {
        string sanitized = string.Concat(key.Select(c => Array.IndexOf(_invalidChars, c) >= 0 ? '_' : c))
            .TrimEnd('.', ' ');

        return sanitized.Length == 0 ? "_" : sanitized;
    }

    private string GetSubdirPath(string subdir)
    {
        return Path.Join(CacheDir, SanitizeKey(subdir));
    }

    private string GetPath(string subdir, string key)
    {
        return Path.Join(GetSubdirPath(subdir), SanitizeKey(key) + ".cache.json");
    }

    private string GetPath(string key)
    {
        return Path.Join(CacheDir, SanitizeKey(key) + ".cache.json");
    }

    private class CacheEntry<T>
    {
        public T? Value { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
