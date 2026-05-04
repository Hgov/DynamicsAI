using DynamicsAI.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace DynamicsAI.Infrastructure.Cache;

public class MetadataCacheService(IMemoryCache memoryCache) : ICacheService
{
    public Task<T?> GetAsync<T>(string key) where T : class
    {
        memoryCache.TryGetValue(key, out T? value);
        return Task.FromResult(value);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl) where T : class
    {
        memoryCache.Set(key, value, ttl);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        memoryCache.Remove(key);
        return Task.CompletedTask;
    }
}
