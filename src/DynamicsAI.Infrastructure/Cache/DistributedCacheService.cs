using System.Text.Json;
using DynamicsAI.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;

namespace DynamicsAI.Infrastructure.Cache;

public class DistributedCacheService(IDistributedCache cache) : ICacheService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        var bytes = await cache.GetAsync(key);
        if (bytes is null) return null;
        return JsonSerializer.Deserialize<T>(bytes, JsonOpts);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl) where T : class
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOpts);
        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl };
        await cache.SetAsync(key, bytes, options);
    }

    public async Task RemoveAsync(string key)
    {
        await cache.RemoveAsync(key);
    }
}
