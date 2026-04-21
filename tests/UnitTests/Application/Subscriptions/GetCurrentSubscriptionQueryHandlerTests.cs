using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Subscriptions.DTOs.Responses;
using FinFlow.Application.Subscriptions.Queries.GetCurrentSubscription;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.TenantSubscriptions;
using FinFlow.Domain.TenantUsageSnapshots;
using FinFlow.Infrastructure.Subscriptions;

namespace FinFlow.UnitTests.Application.Subscriptions;

public sealed class GetCurrentSubscriptionQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Handle_ReturnsCurrentSubscription_WithEntitlementsAndUsage()
    {
        var periodStart = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

        var subscriptionResult = TenantSubscription.Create(TenantId, PlanTier.Pro, periodStart, periodEnd);
        Assert.True(subscriptionResult.IsSuccess, subscriptionResult.Error.Description);

        var usageResult = TenantUsageSnapshot.Create(TenantId, DateOnly.FromDateTime(periodStart), DateOnly.FromDateTime(periodEnd));
        Assert.True(usageResult.IsSuccess, usageResult.Error.Description);

        Assert.True(usageResult.Value.RecordOcrUsage(7).IsSuccess);
        Assert.True(usageResult.Value.RecordChatbotUsage(11).IsSuccess);
        Assert.True(usageResult.Value.SetStorageUsedBytes(1_234).IsSuccess);

        var usageService = new RecordingTenantUsageService(usageResult.Value);
        var subscriptionRepository = new StubTenantSubscriptionRepository(subscriptionResult.Value);
        var handler = CreateHandler(subscriptionRepository, usageService);

        var result = await handler.Handle(new GetCurrentSubscriptionQuery(TenantId), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal("Pro", result.Value.PlanTier);
        Assert.Equal("Active", result.Value.Status);
        Assert.Equal(periodStart, result.Value.CurrentPeriodStart);
        Assert.Equal(periodEnd, result.Value.CurrentPeriodEnd);
        Assert.True(result.Value.Entitlements.DocumentsOcrEnabled);
        Assert.True(result.Value.Entitlements.ChatbotEnabled);
        Assert.Equal(7, result.Value.Usage.OcrPagesUsed);
        Assert.Equal(11, result.Value.Usage.ChatbotMessagesUsed);
        Assert.Equal(1_234, result.Value.Usage.StorageUsedBytes);
    }

    [Fact]
    public async Task Handle_FallsBackToFreePlan_WhenSubscriptionIsMissing()
    {
        var now = DateTime.UtcNow;
        var expectedStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var expectedEnd = expectedStart.AddMonths(1);

        var usageService = new RecordingTenantUsageService();
        var subscriptionRepository = new StubTenantSubscriptionRepository();
        var handler = CreateHandler(subscriptionRepository, usageService);

        var result = await handler.Handle(new GetCurrentSubscriptionQuery(TenantId), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal("Free", result.Value.PlanTier);
        Assert.Equal("Active", result.Value.Status);
        Assert.False(result.Value.Entitlements.DocumentsOcrEnabled);
        Assert.False(result.Value.Entitlements.ChatbotEnabled);
        Assert.Equal(DateOnly.FromDateTime(expectedStart), usageService.LastPeriodStart);
        Assert.Equal(DateOnly.FromDateTime(expectedEnd), usageService.LastPeriodEnd);
        Assert.Equal(0, result.Value.Usage.OcrPagesUsed);
    }

    private static GetCurrentSubscriptionQueryHandler CreateHandler(
        ITenantSubscriptionRepository subscriptionRepository,
        RecordingTenantUsageService usageService)
        => new(subscriptionRepository, new SubscriptionFeatureGate(subscriptionRepository, usageService, new PlanEntitlementCatalog()), usageService);

    private sealed class StubTenantSubscriptionRepository : ITenantSubscriptionRepository
    {
        private readonly TenantSubscription? _subscription;

        public StubTenantSubscriptionRepository(TenantSubscription? subscription = null)
        {
            _subscription = subscription;
        }

        public Task<TenantSubscription?> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default)
            => Task.FromResult(_subscription is not null && _subscription.IdTenant == idTenant ? _subscription : null);

        public void Add(TenantSubscription subscription) => throw new NotSupportedException();
        public void Update(TenantSubscription subscription) => throw new NotSupportedException();
    }

    private sealed class RecordingTenantUsageService : ITenantUsageService
    {
        private readonly TenantUsageSnapshot? _snapshot;

        public RecordingTenantUsageService(TenantUsageSnapshot? snapshot = null)
        {
            _snapshot = snapshot;
        }

        public Guid? LastTenantId { get; private set; }
        public DateOnly? LastPeriodStart { get; private set; }
        public DateOnly? LastPeriodEnd { get; private set; }

        public Task<TenantUsageSnapshot> GetCurrentUsageAsync(
            Guid tenantId,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
        {
            LastTenantId = tenantId;
            LastPeriodStart = periodStart;
            LastPeriodEnd = periodEnd;

            if (_snapshot is not null)
                return Task.FromResult(_snapshot);

            var createResult = TenantUsageSnapshot.Create(tenantId, periodStart, periodEnd);
            Assert.True(createResult.IsSuccess, createResult.Error.Description);
            return Task.FromResult(createResult.Value);
        }

        public Task RecordOcrUsageAsync(Guid tenantId, int pageCount, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordChatbotUsageAsync(Guid tenantId, int messageCount, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetStorageUsedBytesAsync(Guid tenantId, long storageUsedBytes, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
