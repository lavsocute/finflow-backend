using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

namespace FinFlow.Infrastructure.Auth;

public interface ILoginRateLimiter
{
    Task<bool> IsBlockedAsync(string? ip, string email, Guid? tenantId = null);
    Task RecordFailureAsync(string? ip, string email, Guid? tenantId = null);
    Task ResetAccountAsync(string email, Guid? tenantId = null);
}

public class RedisLoginRateLimiter : ILoginRateLimiter
{
    private readonly Lazy<IConnectionMultiplexer> _redisFactory;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<RedisLoginRateLimiter> _logger;
    
    // Cấu hình giới hạn
    private const int MaxAttemptsPerAccount = 5;
    private const int MaxAttemptsPerAccountPerIp = 5;
    private const int MaxAttemptsPerIp = 20;
    private const int BlockDurationMinutes = 15;

    // Lua script atomic
    private static readonly string _recordScript = @"
        local countKey = KEYS[1]
        local blockKey = KEYS[2]
        local limit = tonumber(ARGV[1])
        local window = tonumber(ARGV[2])

        if redis.call('EXISTS', blockKey) == 1 then return -1 end

        local count = redis.call('INCR', countKey)
        if count == 1 then redis.call('EXPIRE', countKey, window) end

        if count >= limit then
            redis.call('DEL', countKey)
            redis.call('SET', blockKey, 'true')
            redis.call('EXPIRE', blockKey, window)
            return -2
        end
        return count
    ";

    public RedisLoginRateLimiter(
        Lazy<IConnectionMultiplexer> redisFactory, 
        IMemoryCache memoryCache,
        ILogger<RedisLoginRateLimiter> logger)
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
            _logger.LogWarning(ex, "Redis connection failed. Falling back to in-memory cache.");
            return null;
        }
    }

    private static string GetTenantPrefix(Guid? tenantId) => tenantId.HasValue ? $"{tenantId.Value}:" : "";

    public async Task<bool> IsBlockedAsync(string? ip, string email, Guid? tenantId = null)
    {
        var normalizedEmail = NormalizeEmail(email);
        var emailHash = HashKey(normalizedEmail);
        var ipHash = ip != null ? HashKey(ip) : null;
        var tenantPrefix = GetTenantPrefix(tenantId);

        bool isBlocked = false;
        var redis = GetRedis();

        if (redis != null)
        {
            try
            {
                // 1. Global Account Block (Scoped by Tenant if available)
                isBlocked = await redis.KeyExistsAsync($"Block:Acc:{tenantPrefix}{emailHash}");
                if (isBlocked) return true;

                // 2. IP-Specific Account Block
                if (ipHash != null)
                {
                    isBlocked = await redis.KeyExistsAsync($"Block:Acc:{tenantPrefix}{emailHash}:Ip:{ipHash}");
                    if (isBlocked) return true;
                }
                
                // 3. Global IP Block
                if (ipHash != null)
                {
                    isBlocked = await redis.KeyExistsAsync($"Block:Ip:{ipHash}");
                    if (isBlocked) return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis error during IsBlockedAsync. Checking fallback.");
            }
        }

        // Fallback Memory Check
        if (_memoryCache.TryGetValue($"Block:Acc:{tenantPrefix}{normalizedEmail}", out _)) return true;
        if (ip != null && _memoryCache.TryGetValue($"Block:Acc:{tenantPrefix}{normalizedEmail}:Ip:{ip}", out _)) return true;
        if (ip != null && _memoryCache.TryGetValue($"Block:Ip:{ip}", out _)) return true;

        return isBlocked;
    }

    public async Task RecordFailureAsync(string? ip, string email, Guid? tenantId = null)
    {
        var normalizedEmail = NormalizeEmail(email);
        var emailHash = HashKey(normalizedEmail);
        var ipHash = ip != null ? HashKey(ip) : null;
        var expirySeconds = BlockDurationMinutes * 60;
        var tenantPrefix = GetTenantPrefix(tenantId);

        var redis = GetRedis();
        if (redis != null)
        {
            try
            {
                // 1. Global Account Limit
                await redis.ScriptEvaluateAsync(_recordScript, 
                    new RedisKey[] { $"Fail:Acc:{tenantPrefix}{emailHash}", $"Block:Acc:{tenantPrefix}{emailHash}" }, 
                    new RedisValue[] { MaxAttemptsPerAccount, expirySeconds });

                // 2. IP-Specific Account Limit
                if (ipHash != null)
                {
                    await redis.ScriptEvaluateAsync(_recordScript, 
                        new RedisKey[] { $"Fail:Acc:{tenantPrefix}{emailHash}:Ip:{ipHash}", $"Block:Acc:{tenantPrefix}{emailHash}:Ip:{ipHash}" }, 
                        new RedisValue[] { MaxAttemptsPerAccountPerIp, expirySeconds });

                    // 3. Global IP Limit
                    await redis.ScriptEvaluateAsync(_recordScript, 
                        new RedisKey[] { $"Fail:Ip:{ipHash}", $"Block:Ip:{ipHash}" }, 
                        new RedisValue[] { MaxAttemptsPerIp, expirySeconds });
                }
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis error during RecordFailureAsync. Falling back to memory.");
            }
        }

        // Fallback Memory Logic
        var accKey = $"Fail:Acc:{tenantPrefix}{normalizedEmail}";
        var accCount = _memoryCache.TryGetValue<int>(accKey, out var c1) ? c1 + 1 : 1;
        _memoryCache.Set(accKey, accCount, TimeSpan.FromMinutes(BlockDurationMinutes));
        if (accCount >= MaxAttemptsPerAccount)
            _memoryCache.Set($"Block:Acc:{tenantPrefix}{normalizedEmail}", true, TimeSpan.FromMinutes(BlockDurationMinutes));

        if (ip != null)
        {
            var accIpKey = $"Fail:Acc:{tenantPrefix}{normalizedEmail}:Ip:{ip}";
            var accIpCount = _memoryCache.TryGetValue<int>(accIpKey, out var c2) ? c2 + 1 : 1;
            _memoryCache.Set(accIpKey, accIpCount, TimeSpan.FromMinutes(BlockDurationMinutes));
            if (accIpCount >= MaxAttemptsPerAccountPerIp)
                _memoryCache.Set($"Block:Acc:{tenantPrefix}{normalizedEmail}:Ip:{ip}", true, TimeSpan.FromMinutes(BlockDurationMinutes));

            var ipKey = $"Fail:Ip:{ip}";
            var ipCount = _memoryCache.TryGetValue<int>(ipKey, out var c3) ? c3 + 1 : 1;
            _memoryCache.Set(ipKey, ipCount, TimeSpan.FromMinutes(BlockDurationMinutes));
            if (ipCount >= MaxAttemptsPerIp)
                _memoryCache.Set($"Block:Ip:{ip}", true, TimeSpan.FromMinutes(BlockDurationMinutes));
        }
    }

    public async Task ResetAccountAsync(string email, Guid? tenantId = null)
    {
        var normalizedEmail = NormalizeEmail(email);
        var emailHash = HashKey(normalizedEmail);
        var tenantPrefix = GetTenantPrefix(tenantId);
        var redis = GetRedis();

        // 1. Reset Global Account Keys
        if (redis != null)
        {
            try
            {
                await redis.KeyDeleteAsync($"Fail:Acc:{tenantPrefix}{emailHash}");
                await redis.KeyDeleteAsync($"Block:Acc:{tenantPrefix}{emailHash}");
            }
            catch { /* Ignore */ }
        }

        // 2. Reset Memory Fallback Keys
        _memoryCache.Remove($"Fail:Acc:{tenantPrefix}{normalizedEmail}");
        _memoryCache.Remove($"Block:Acc:{tenantPrefix}{normalizedEmail}");
    }

    private static string NormalizeEmail(string email) => 
        (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string HashKey(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes);
    }
}
