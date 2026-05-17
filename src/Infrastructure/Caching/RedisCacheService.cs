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

    public async Task RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyPrefix))
            return;

        var redis = GetRedis();
        if (redis != null)
        {
            try
            {
                // Iterate over all servers and SCAN matching keys.
                // Pattern wraps with wildcard suffix so prefix-only match is straightforward.
                var pattern = keyPrefix + "*";
                foreach (var endpoint in _redisFactory.Value.GetEndPoints())
                {
                    var server = _redisFactory.Value.GetServer(endpoint);
                    var keys = server.Keys(pattern: pattern, pageSize: 250);
                    var batch = new List<RedisKey>();
                    foreach (var k in keys)
                    {
                        batch.Add(k);
                        if (batch.Count >= 250)
                        {
                            await redis.KeyDeleteAsync(batch.ToArray());
                            batch.Clear();
                        }
                    }
                    if (batch.Count > 0)
                        await redis.KeyDeleteAsync(batch.ToArray());
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis SCAN/DELETE failed for prefix {Prefix}.", keyPrefix);
            }
        }

        // IMemoryCache does not support prefix removal natively; relies on TTL.
        // For tests / dev with no Redis, callers should manage cache lifecycle explicitly.
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

    public async Task<long> IncrementWithExpiryAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        var redis = GetRedis();
        if (redis != null)
        {
            try
            {
                // INCR + EXPIRE only when counter was just created (value == 1).
                // This implements a fixed-window counter without resetting TTL on every hit.
                var newValue = await redis.StringIncrementAsync(key);
                if (newValue == 1)
                    await redis.KeyExpireAsync(key, expiry);
                return newValue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis INCR failed for key {Key}; falling back to memory cache.", key);
            }
        }

        // Memory-cache fallback (single-process). Stored as boxed long.
        if (_memoryCache.TryGetValue<long>(key, out var current))
        {
            var next = current + 1;
            _memoryCache.Set(key, next, expiry);
            return next;
        }
        _memoryCache.Set(key, 1L, expiry);
        return 1;
    }

}
