using System;
using System.IO;
using System.Text.Json;

namespace Coclico.Services
{
    public class CacheService : ICacheService
    {
        private static CacheService? _instance;
        public static CacheService Instance => _instance ??= new CacheService();

        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico", "cache");

        private static readonly JsonSerializerOptions _serializeOptions = new() { WriteIndented = false };

        private CacheService()
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
                File.WriteAllText(GetPath(key), JsonSerializer.Serialize(entry, _serializeOptions));
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

                var entry = JsonSerializer.Deserialize<CacheEntry<T>>(File.ReadAllText(path));
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

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
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
                    Value     = value,
                    ExpiresAt = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : DateTime.MaxValue,
                    CreatedAt = DateTime.UtcNow
                };
                File.WriteAllText(GetPath(subdir, key),
                    JsonSerializer.Serialize(entry, _serializeOptions));
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
                var entry = JsonSerializer.Deserialize<CacheEntry<T>>(File.ReadAllText(path));
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
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
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
            try { File.Delete(GetPath(subdir, key)); } catch (Exception ex) { LoggingService.LogException(ex, "CacheService.Invalidate(subdir)"); }
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
            try { File.Delete(GetPath(key)); } catch (Exception ex) { LoggingService.LogException(ex, "CacheService.Invalidate(key)"); }
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

        public async System.Threading.Tasks.Task SetAsync<T>(string key, T value, TimeSpan? ttl = null)
        {
            try
            {
                var entry = new CacheEntry<T>
                {
                    Value = value,
                    ExpiresAt = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : DateTime.MaxValue,
                    CreatedAt = DateTime.UtcNow
                };
                var json = System.Text.Json.JsonSerializer.Serialize(entry, _serializeOptions);
                await System.IO.File.WriteAllTextAsync(GetPath(key), json, System.Text.Encoding.UTF8).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "CacheService.SetAsync");
            }
        }

        public async System.Threading.Tasks.Task<T?> GetAsync<T>(string key)
        {
            try
            {
                var path = GetPath(key);
                if (!System.IO.File.Exists(path)) return default;
                var json = await System.IO.File.ReadAllTextAsync(path, System.Text.Encoding.UTF8).ConfigureAwait(false);
                var entry = System.Text.Json.JsonSerializer.Deserialize<CacheEntry<T>>(json);
                if (entry == null) return default;
                if (DateTime.UtcNow > entry.ExpiresAt) { System.IO.File.Delete(path); return default; }
                return entry.Value;
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "CacheService.GetAsync");
                return default;
            }
        }

        public async System.Threading.Tasks.Task<bool> HasAsync(string key)
        {
            try
            {
                var path = GetPath(key);
                if (!System.IO.File.Exists(path)) return false;
                var json = await System.IO.File.ReadAllTextAsync(path, System.Text.Encoding.UTF8).ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("ExpiresAt", out var expiryEl))
                {
                    if (DateTime.TryParse(expiryEl.GetString(), out var expiry))
                        return DateTime.UtcNow <= expiry;
                }
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "CacheService.HasAsync");
                return false;
            }
        }

        public async System.Threading.Tasks.Task InvalidateAsync(string key)
        {
            try
            {
                var path = GetPath(key);
                await System.Threading.Tasks.Task.Run(() =>
                {
                    if (File.Exists(path)) File.Delete(path);
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { LoggingService.LogException(ex, "CacheService.InvalidateAsync"); }
        }

        public async System.Threading.Tasks.Task ClearAsync()
        {
            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    foreach (var f in Directory.GetFiles(CacheDir, "*.json", SearchOption.AllDirectories))
                        File.Delete(f);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "CacheService.ClearAsync");
            }
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
}
