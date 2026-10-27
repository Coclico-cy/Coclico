namespace Coclico.Services;

public interface ICacheService
{
    void Set<T>(string key, T value, TimeSpan? ttl = null);
    T? Get<T>(string key);
    bool TryGet<T>(string key, out T? value);
    bool Has(string key);
    void SetInSubdir<T>(string subdir, string key, T value, TimeSpan? ttl = null);
    T? GetFromSubdir<T>(string subdir, string key);
    bool HasInSubdir(string subdir, string key);
    void InvalidateInSubdir(string subdir, string key);
    void ClearSubdir(string subdir);
    void Invalidate(string key);
    void Clear();
    long GetCacheSizeBytes();

    T GetOrSet<T>(string key, Func<T> factory, TimeSpan? ttl = null);
    T GetOrSetInSubdir<T>(string subdir, string key, Func<T> factory, TimeSpan? ttl = null);

    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null);
    Task<T?> GetAsync<T>(string key);
    Task<(bool Found, T? Value)> TryGetAsync<T>(string key);
    Task<bool> HasAsync(string key);
    Task InvalidateAsync(string key);
    Task ClearAsync();

    Task SetInSubdirAsync<T>(string subdir, string key, T value, TimeSpan? ttl = null);
    Task<T?> GetFromSubdirAsync<T>(string subdir, string key);
    Task InvalidateInSubdirAsync(string subdir, string key);
    Task ClearSubdirAsync(string subdir);
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null);
    Task<T> GetOrSetInSubdirAsync<T>(string subdir, string key, Func<Task<T>> factory, TimeSpan? ttl = null);
}
