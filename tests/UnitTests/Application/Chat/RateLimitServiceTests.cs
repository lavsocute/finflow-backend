using FinFlow.Application.Chat.Services;
using FinFlow.Application.Common.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinFlow.UnitTests.Application.Chat;

public sealed class RateLimitServiceTests
{
    [Fact]
    public async Task CheckUserRateLimitAsync_UsesOneSecondWindow_ForFastChatFollowUps()
    {
        var cache = new RecordingCounterCache();
        var service = new RateLimitService(cache, NullLogger<RateLimitService>.Instance);

        var allowed = await service.CheckUserRateLimitAsync(Guid.NewGuid());

        Assert.True(allowed);
        Assert.Equal(TimeSpan.FromSeconds(1), cache.LastExpiry);
    }

    [Fact]
    public async Task CheckUserRateLimitAsync_UsesConfiguredWindow_WhenConfigured()
    {
        var cache = new RecordingCounterCache();
        var service = new RateLimitService(
            cache,
            NullLogger<RateLimitService>.Instance,
            Options.Create(new ChatRateLimitOptions { PerUserWindowSeconds = 2 }));

        var allowed = await service.CheckUserRateLimitAsync(Guid.NewGuid());

        Assert.True(allowed);
        Assert.Equal(TimeSpan.FromSeconds(2), cache.LastExpiry);
    }

    [Fact]
    public async Task CheckUserRateLimitAsync_BlocksSecondRequestInsideCurrentWindow()
    {
        var cache = new RecordingCounterCache();
        var service = new RateLimitService(cache, NullLogger<RateLimitService>.Instance);
        var membershipId = Guid.NewGuid();

        var first = await service.CheckUserRateLimitAsync(membershipId);
        var second = await service.CheckUserRateLimitAsync(membershipId);

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task CheckTenantRateLimitAsync_AllowsSixtyRequestsPerMinute_ThenBlocks()
    {
        var cache = new RecordingCounterCache();
        var service = new RateLimitService(cache, NullLogger<RateLimitService>.Instance);
        var tenantId = Guid.NewGuid();

        for (var i = 0; i < 60; i++)
            Assert.True(await service.CheckTenantRateLimitAsync(tenantId));

        Assert.False(await service.CheckTenantRateLimitAsync(tenantId));
        Assert.Equal(TimeSpan.FromSeconds(60), cache.LastExpiry);
    }

    private sealed class RecordingCounterCache : ICacheService
    {
        private readonly Dictionary<string, long> _counters = [];

        public TimeSpan? LastExpiry { get; private set; }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class =>
            Task.FromResult<T?>(null);

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class =>
            await factory();

        public Task<long> IncrementWithExpiryAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            LastExpiry = expiry;
            _counters.TryGetValue(key, out var current);
            var next = current + 1;
            _counters[key] = next;
            return Task.FromResult(next);
        }
    }
}
