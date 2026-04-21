using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.TenantSubscriptions;
using FinFlow.Domain.TenantUsageSnapshots;
using FinFlow.Infrastructure.Subscriptions;

namespace FinFlow.UnitTests.Infrastructure.Subscriptions;

public sealed class SubscriptionFeatureGateTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task EnsureOcrAllowedAsync_ReturnsQuotaExceeded_WhenUsageReachedMonthlyLimit()
    {
        var gate = CreateGate(
            PlanTier.Pro,
            ocrPagesUsed: 1_000);

        var result = await gate.EnsureOcrAllowedAsync(TenantId, 1, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.OcrQuotaExceeded", result.Error.Code);
    }

    [Fact]
    public async Task EnsureOcrAllowedAsync_ReturnsOcrNotAvailable_WhenPlanDoesNotIncludeOcr()
    {
        var gate = CreateGate(PlanTier.Free);

        var result = await gate.EnsureOcrAllowedAsync(TenantId, 1, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Documents.OcrNotAvailableForCurrentPlan", result.Error.Code);
    }

    [Fact]
    public async Task EnsureChatbotAllowedAsync_ReturnsSuccess_WhenUsageBelowLimit()
    {
        var gate = CreateGate(
            PlanTier.Pro,
            chatbotMessagesUsed: 42);

        var result = await gate.EnsureChatbotAllowedAsync(TenantId, 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EnsureChatbotAllowedAsync_ReturnsQuotaExceeded_WhenUsageReachedMonthlyLimit()
    {
        var gate = CreateGate(
            PlanTier.Pro,
            chatbotMessagesUsed: 10_000);

        var result = await gate.EnsureChatbotAllowedAsync(TenantId, 1, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.ChatbotQuotaExceeded", result.Error.Code);
    }

    [Fact]
    public async Task GetEntitlementsAsync_ReturnsFreeEntitlements_WhenSubscriptionMissing()
    {
        var gate = new SubscriptionFeatureGate(
            new StubTenantSubscriptionRepository(),
            new StubTenantUsageService(),
            new PlanEntitlementCatalog());

        var entitlements = await gate.GetEntitlementsAsync(TenantId, CancellationToken.None);

        Assert.True(entitlements.DocumentsManualEntryEnabled);
        Assert.False(entitlements.DocumentsOcrEnabled);
        Assert.False(entitlements.ChatbotEnabled);
        Assert.Equal(0, entitlements.MonthlyOcrPages);
        Assert.Equal(0, entitlements.MonthlyChatbotMessages);
    }

    [Fact]
    public async Task GetEntitlementsAsync_ReturnsFreeEntitlements_WhenSubscriptionIsNotActive()
    {
        var gate = CreateGate(PlanTier.Enterprise, status: SubscriptionStatus.Canceled);

        var entitlements = await gate.GetEntitlementsAsync(TenantId, CancellationToken.None);

        Assert.True(entitlements.DocumentsManualEntryEnabled);
        Assert.False(entitlements.DocumentsOcrEnabled);
        Assert.False(entitlements.ChatbotEnabled);
        Assert.Equal(0, entitlements.MonthlyOcrPages);
        Assert.Equal(0, entitlements.MonthlyChatbotMessages);
    }

    private static SubscriptionFeatureGate CreateGate(
        PlanTier planTier,
        SubscriptionStatus status = SubscriptionStatus.Active,
        int ocrPagesUsed = 0,
        int chatbotMessagesUsed = 0)
    {
        var subscriptionResult = TenantSubscription.Create(
            TenantId,
            planTier,
            DateTime.SpecifyKind(new DateTime(2026, 4, 1), DateTimeKind.Utc),
            DateTime.SpecifyKind(new DateTime(2026, 4, 30), DateTimeKind.Utc));

        Assert.True(subscriptionResult.IsSuccess, subscriptionResult.Error.Description);

        var subscription = subscriptionResult.Value;
        if (status != SubscriptionStatus.Active)
        {
            SetSubscriptionStatus(subscription, status);
        }

        var usageResult = TenantUsageSnapshot.Create(
            TenantId,
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 4, 30));

        Assert.True(usageResult.IsSuccess, usageResult.Error.Description);

        var usage = usageResult.Value;

        if (ocrPagesUsed > 0)
        {
            var recordOcrUsageResult = usage.RecordOcrUsage(ocrPagesUsed);
            Assert.True(recordOcrUsageResult.IsSuccess, recordOcrUsageResult.Error.Description);
        }

        if (chatbotMessagesUsed > 0)
        {
            var recordChatbotUsageResult = usage.RecordChatbotUsage(chatbotMessagesUsed);
            Assert.True(recordChatbotUsageResult.IsSuccess, recordChatbotUsageResult.Error.Description);
        }

        return new SubscriptionFeatureGate(
            new StubTenantSubscriptionRepository(subscription),
            new StubTenantUsageService(usage),
            new PlanEntitlementCatalog());
    }

    private static void SetSubscriptionStatus(TenantSubscription subscription, SubscriptionStatus status)
    {
        var property = typeof(TenantSubscription).GetProperty(nameof(TenantSubscription.Status));
        Assert.NotNull(property);
        property.SetValue(subscription, status);
    }

    private sealed class StubTenantSubscriptionRepository : ITenantSubscriptionRepository
    {
        private readonly TenantSubscription? _subscription;

        public StubTenantSubscriptionRepository(TenantSubscription? subscription = null)
        {
            _subscription = subscription;
        }

        public Task<TenantSubscription?> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default)
            => Task.FromResult(_subscription);

        public void Add(TenantSubscription subscription) => throw new NotSupportedException();

        public void Update(TenantSubscription subscription) => throw new NotSupportedException();
    }

    private sealed class StubTenantUsageService : ITenantUsageService
    {
        private readonly TenantUsageSnapshot? _usage;

        public StubTenantUsageService(TenantUsageSnapshot? usage = null)
        {
            _usage = usage;
        }

        public Task<TenantUsageSnapshot> GetCurrentUsageAsync(
            Guid tenantId,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
        {
            if (_usage is not null)
                return Task.FromResult(_usage);

            var createResult = TenantUsageSnapshot.Create(tenantId, periodStart, periodEnd);
            if (createResult.IsFailure)
                throw new InvalidOperationException(createResult.Error.Description);

            return Task.FromResult(createResult.Value);
        }

        public Task RecordOcrUsageAsync(
            Guid tenantId,
            int pageCount,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordChatbotUsageAsync(
            Guid tenantId,
            int messageCount,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetStorageUsedBytesAsync(
            Guid tenantId,
            long storageUsedBytes,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
