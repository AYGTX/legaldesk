using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;
using StackExchange.Redis;

namespace SignFlow.Infrastructure.Services.Cache;

public class RedisCacheService(IDistributedCache cache, ILogger<RedisCacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var bytes = await cache.GetAsync(key, cancellationToken);
            return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache get failed for key {Key}", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var options = new DistributedCacheEntryOptions();
            if (expiry.HasValue) options.AbsoluteExpirationRelativeToNow = expiry;

            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            await cache.SetAsync(key, bytes, options, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache set failed for key {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache remove failed for key {Key}", key);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        // This requires direct Redis access to scan keys — implement via IConnectionMultiplexer in production
        logger.LogDebug("RemoveByPrefix called for prefix {Prefix}", prefix);
        await Task.CompletedTask;
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
    {
        var cached = await GetAsync<T>(key, cancellationToken);
        if (cached is not null) return cached;

        var value = await factory();
        await SetAsync(key, value, expiry, cancellationToken);
        return value;
    }
}
