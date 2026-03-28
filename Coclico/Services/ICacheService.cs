#nullable enable
using System;
using System.Threading.Tasks;

namespace Coclico.Services;

public interface ICacheService
{
    void Set<T>(string key, T value, TimeSpan? ttl = null);
    T? Get<T>(string key);
    bool Has(string key);
    void Set<T>(string subdir, string key, T value, TimeSpan? ttl = null);
    T? Get<T>(string subdir, string key);
    bool Has(string subdir, string key);
    void Invalidate(string subdir, string key);
    void ClearSubdir(string subdir);
    void Invalidate(string key);
    void Clear();
    long GetCacheSizeBytes();

    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null);
    Task<T?> GetAsync<T>(string key);
    Task<bool> HasAsync(string key);
    Task InvalidateAsync(string key);
    Task ClearAsync();
}
