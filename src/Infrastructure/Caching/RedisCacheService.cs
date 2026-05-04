using FinFlow.Application.Common.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FinFlow.Infrastructure.Caching;

public class RedisCacheService : ICacheService
{
    private readonly Lazy<IConnectionMultiplexer> _redisFactory;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly TimeSpan _defaultExpiration = TimeSpan.FromMinutes(5);

    public RedisCacheService(
        Lazy<IConnectionMultiplexer> redisFactory,
        IMemoryCache memoryCache,
        ILogger<RedisCacheService> logger)
    {
        _redisFactory = redisFactory;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    private IDatabase? GetRedis()
    {
        try
        {
            var redis = _redisFactory.Value;
            if (redis.IsConnected) return redis.GetDatabase();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis connection failed. Using in-memory cache.");
            return null;
        }
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        var redis = GetRedis();
        if (redis != null)
        {
            try
            {
                var value = await redis.StringGetAsync(key);
                if (value.HasValue)
                {
                    return System.Text.Json.JsonSerializer.Deserialize<T>(value!);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis GET failed for key {Key}. Using memory cache.", key);
            }
        }

        if (_memoryCache.TryGetValue<T>(key, out var memoryValue))
        {
            return memoryValue;
        }

        return null;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
    {
        var exp = expiration ?? _defaultExpiration;
        var serialized = System.Text.Json.JsonSerializer.Serialize(value);

        var redis = GetRedis();
        if (redis != null)
        {
            try
            {
                await redis.StringSetAsync(key, serialized, exp);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis SET failed for key {Key}. Using memory cache.", key);
            }
        }

        _memoryCache.Set(key, value, exp);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var redis = GetRedis();
        if (redis != null)
        {
            try
            {
                await redis.KeyDeleteAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis DELETE failed for key {Key}.", key);
            }
        }

        _memoryCache.Remove(key);
    }

    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
    {
        var cached = await GetAsync<T>(key, cancellationToken);
        if (cached != null)
            return cached;

        var value = await factory();
        await SetAsync(key, value, expiration, cancellationToken);
        return value;
    }
}