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
        Assert.Equal(0, entitlements.WorkspaceMonthlyOcrPages);
        Assert.Equal(0, entitlements.MemberMonthlyOcrPages);
        Assert.Equal(0, entitlements.WorkspaceMonthlyChatbotMessages);
        Assert.Equal(0, entitlements.MemberMonthlyChatbotMessages);
    }

    [Fact]
    public async Task GetEntitlementsAsync_ReturnsFreeEntitlements_WhenSubscriptionIsCanceled()
    {
        var gate = CreateGate(PlanTier.Enterprise, status: SubscriptionStatus.Canceled);

        var entitlements = await gate.GetEntitlementsAsync(TenantId, CancellationToken.None);

        Assert.True(entitlements.DocumentsManualEntryEnabled);
        Assert.False(entitlements.DocumentsOcrEnabled);
        Assert.False(entitlements.ChatbotEnabled);
    }

    [Fact]
    public async Task GetEntitlementsAsync_ReturnsFreeEntitlements_WhenSubscriptionIsExpired()
    {
        // Subscription where now is past PeriodEnd + GracePeriodDays — effective status = Expired
        var gate = CreateGate(
            PlanTier.Pro,
            periodStart: DateTime.SpecifyKind(new DateTime(2025, 1, 1), DateTimeKind.Utc),
            periodEnd: DateTime.SpecifyKind(new DateTime(2025, 1, 31), DateTimeKind.Utc));

        var entitlements = await gate.GetEntitlementsAsync(TenantId, CancellationToken.None);

        // Pro entitlements should NOT apply because subscription period is way past + grace
        Assert.False(entitlements.DocumentsOcrEnabled);
        Assert.False(entitlements.ChatbotEnabled);
    }

    [Fact]
    public async Task GetEntitlementsAsync_ReturnsProEntitlements_WhenSubscriptionIsActive()
    {
        // Period that includes "now" — effective status = Active
        var gate = CreateGate(
            PlanTier.Pro,
            periodStart: DateTime.UtcNow.AddDays(-1),
            periodEnd: DateTime.UtcNow.AddDays(29));

        var entitlements = await gate.GetEntitlementsAsync(TenantId, CancellationToken.None);

        Assert.True(entitlements.DocumentsOcrEnabled);
        Assert.True(entitlements.ChatbotEnabled);
        Assert.Equal(1_000, entitlements.WorkspaceMonthlyOcrPages);
        Assert.Equal(100, entitlements.MemberMonthlyOcrPages);
    }

    [Fact]
    public async Task EnsureFeatureEnabledAsync_ReturnsSuccess_WhenFeatureAvailable()
    {
        var gate = CreateGate(
            PlanTier.Pro,
            periodStart: DateTime.UtcNow.AddDays(-1),
            periodEnd: DateTime.UtcNow.AddDays(29));

        var result = await gate.EnsureFeatureEnabledAsync(TenantId, SubscriptionFeature.DocumentsOcr, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EnsureFeatureEnabledAsync_ReturnsFailure_WhenPlanDoesNotIncludeOcr()
    {
        var gate = CreateGate(PlanTier.Free);

        var result = await gate.EnsureFeatureEnabledAsync(TenantId, SubscriptionFeature.DocumentsOcr, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Documents.OcrNotAvailableForCurrentPlan", result.Error.Code);
    }

    private static SubscriptionFeatureGate CreateGate(
        PlanTier planTier,
        SubscriptionStatus status = SubscriptionStatus.Active,
        DateTime? periodStart = null,
        DateTime? periodEnd = null)
    {
        var subscriptionResult = TenantSubscription.Create(
            TenantId,
            planTier,
            periodStart ?? DateTime.SpecifyKind(new DateTime(2026, 4, 1), DateTimeKind.Utc),
            periodEnd ?? DateTime.SpecifyKind(new DateTime(2026, 4, 30), DateTimeKind.Utc));

        Assert.True(subscriptionResult.IsSuccess, subscriptionResult.Error.Description);

        var subscription = subscriptionResult.Value;
        if (status != SubscriptionStatus.Active)
        {
            SetSubscriptionStatus(subscription, status);
        }

        return new SubscriptionFeatureGate(
            new StubTenantSubscriptionRepository(subscription),
            new StubTenantUsageService(),
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
        public Task<TenantUsageSnapshot> GetCurrentUsageAsync(
            Guid tenantId,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
        {
            var createResult = TenantUsageSnapshot.Create(tenantId, periodStart, periodEnd);
            if (createResult.IsFailure)
                throw new InvalidOperationException(createResult.Error.Description);

            return Task.FromResult(createResult.Value);
        }

        public Task RecordOcrUsageAsync(Guid tenantId, int pageCount, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordChatbotUsageAsync(Guid tenantId, int messageCount, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordChatbotTokensAsync(Guid tenantId, long tokensUsed, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetStorageUsedBytesAsync(Guid tenantId, long storageUsedBytes, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
