using FinFlow.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FinFlow.Infrastructure.Auth;

public sealed class RedisOtpOperationLockService : IOtpOperationLockService
{
    private readonly Lazy<IConnectionMultiplexer> _redisFactory;
    private readonly ILogger<RedisOtpOperationLockService> _logger;

    private static readonly string _acquireLockScript = @"
        if redis.call('EXISTS', KEYS[1]) == 1 then
            return 0
        end
        redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[2])
        return 1
    ";

    private const string LockValuePrefix = "otp-lock:";

    public RedisOtpOperationLockService(
        Lazy<IConnectionMultiplexer> redisFactory,
        ILogger<RedisOtpOperationLockService> logger)
    {
        _redisFactory = redisFactory;
        _logger = logger;
    }

    public async Task<IAsyncDisposable?> AcquireLockAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        var redisKey = $"{LockValuePrefix}{key}";
        var lockValue = Guid.NewGuid().ToString();
        var expiryMs = (int)expiry.TotalMilliseconds;

        try
        {
            var redis = GetRedis();
            if (redis == null)
            {
                _logger.LogWarning("Redis unavailable for OTP lock, proceeding without lock");
                return null;
            }

            var result = (int)await redis.ScriptEvaluateAsync(
                _acquireLockScript,
                new RedisKey[] { redisKey },
                new RedisValue[] { lockValue, expiryMs });

            if (result == 0)
            {
                _logger.LogDebug("Failed to acquire OTP lock for key {Key}", key);
                return null;
            }

            _logger.LogDebug("Acquired OTP lock for key {Key}", key);
            return OtpLock.Create(async () =>
            {
                try
                {
                    var deleteScript = @"
                        if redis.call('GET', KEYS[1]) == ARGV[1] then
                            return redis.call('DEL', KEYS[1])
                        end
                        return 0
                    ";
                    await redis.ScriptEvaluateAsync(
                        deleteScript,
                        new RedisKey[] { redisKey },
                        new RedisValue[] { lockValue });
                    _logger.LogDebug("Released OTP lock for key {Key}", key);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to release OTP lock for key {Key}", key);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring OTP lock for key {Key}", key);
            return null;
        }
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
            _logger.LogWarning(ex, "Redis connection failed");
            return null;
        }
    }
}