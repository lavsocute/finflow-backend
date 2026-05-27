using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Chat.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinFlow.Application.Chat.Services;

public sealed class ChatRateLimitOptions
{
    public const string SectionName = "ChatRateLimit";

    public int PerUserWindowSeconds { get; init; } = 1;
    public int TenantWindowSeconds { get; init; } = 60;
    public int TenantMaxRequests { get; init; } = 60;
}

public sealed class RateLimitService : IRateLimitService
{
    private readonly ICacheService _cacheService;
    private readonly ILogger<RateLimitService> _logger;
    private readonly ChatRateLimitOptions _options;

    public RateLimitService(ICacheService cacheService, ILogger<RateLimitService> logger)
        : this(cacheService, logger, Options.Create(new ChatRateLimitOptions()))
    {
    }

    public RateLimitService(
        ICacheService cacheService,
        ILogger<RateLimitService> logger,
        IOptions<ChatRateLimitOptions> options)
    {
        _cacheService = cacheService;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<bool> CheckUserRateLimitAsync(Guid membershipId, CancellationToken ct = default)
    {
        var rateLimitKey = $"chat:ratelimit:{membershipId}";
        var perUserCount = await _cacheService.IncrementWithExpiryAsync(
            rateLimitKey,
            TimeSpan.FromSeconds(Math.Max(1, _options.PerUserWindowSeconds)),
            ct);

        if (perUserCount > 1)
        {
            _logger.LogWarning("Per-user rate limit exceeded for membership {MembershipId}", membershipId);
            return false;
        }

        return true;
    }

    public async Task<bool> CheckTenantRateLimitAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenantRateKey = $"chat:ratelimit:tenant:{tenantId}";
        var tenantCount = await _cacheService.IncrementWithExpiryAsync(
            tenantRateKey,
            TimeSpan.FromSeconds(Math.Max(1, _options.TenantWindowSeconds)),
            ct);

        if (tenantCount > Math.Max(1, _options.TenantMaxRequests))
        {
            _logger.LogWarning("Tenant rate limit exceeded for tenant {TenantId}", tenantId);
            return false;
        }

        return true;
    }
}
