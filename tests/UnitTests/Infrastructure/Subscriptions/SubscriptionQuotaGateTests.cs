using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Subscriptions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.TenantSubscriptions;
using FinFlow.Infrastructure.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinFlow.UnitTests.Infrastructure.Subscriptions;

public sealed class SubscriptionQuotaGateTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MembershipId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    // Use dynamic period centered around UtcNow so subscription is always Active during tests.
    private static readonly DateTime SubscriptionPeriodStart = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-1), DateTimeKind.Utc);
    private static readonly DateTime SubscriptionPeriodEnd = SubscriptionPeriodStart.AddMonths(1);
    private static readonly DateOnly PeriodStart = DateOnly.FromDateTime(SubscriptionPeriodStart);
    private static readonly DateOnly PeriodEnd = DateOnly.FromDateTime(SubscriptionPeriodEnd);

    [Fact]
    public async Task EnsureChatbotAllowedAsync_Fails_WhenWorkspaceQuotaWouldBeExceeded()
    {
        var gate = CreateGate(
            planTier: PlanTier.Pro,
            tenantChatUsed: 10_000,
            memberChatUsed: 0);

        var result = await gate.EnsureChatbotAllowedAsync(TenantId, MembershipId, 1, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.ChatbotQuotaExceeded", result.Error.Code);
    }

    [Fact]
    public async Task EnsureChatbotAllowedAsync_Fails_WhenMessageCountIsNotPositive()
    {
        var gate = CreateGate(planTier: PlanTier.Pro);

        var result = await gate.EnsureChatbotAllowedAsync(TenantId, MembershipId, 0, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.ChatbotMessageCountInvalid", result.Error.Code);
    }

    [Fact]
    public async Task EnsureOcrAllowedAsync_Fails_WhenMemberQuotaWouldBeExceeded()
    {
        var gate = CreateGate(
            planTier: PlanTier.Pro,
            tenantOcrUsed: 0,
            memberOcrUsed: 100);

        var result = await gate.EnsureOcrAllowedAsync(TenantId, MembershipId, 1, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.OcrMemberQuotaExceeded", result.Error.Code);
    }

    [Fact]
    public async Task EnsureOcrAllowedAsync_Fails_WhenPageCountIsNotPositive()
    {
        var gate = CreateGate(planTier: PlanTier.Pro);

        var result = await gate.EnsureOcrAllowedAsync(TenantId, MembershipId, 0, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.OcrPageCountInvalid", result.Error.Code);
    }

    [Fact]
    public async Task EnsureChatbotAllowedAsync_Fails_WhenFeatureDisabled()
    {
        var gate = CreateGate(planTier: PlanTier.Free);

        var result = await gate.EnsureChatbotAllowedAsync(TenantId, MembershipId, 1, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.ChatbotNotAvailableForCurrentPlan", result.Error.Code);
    }

    [Fact]
    public async Task EnsureOcrAllowedAsync_Fails_WhenSubscriptionIsMissing()
    {
        var gate = CreateGateWithSubscription(
            subscription: null,
            tenantUsageService: CreateTenantUsageService(0, 0),
            memberUsageService: CreateMemberUsageService(0, 0));

        var result = await gate.EnsureOcrAllowedAsync(TenantId, MembershipId, 1, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Documents.OcrNotAvailableForCurrentPlan", result.Error.Code);
    }

    [Fact]
    public async Task EnsureChatbotAllowedAsync_Fails_WhenSubscriptionIsInactive()
    {
        var gate = CreateGate(
            planTier: PlanTier.Pro,
            status: SubscriptionStatus.Canceled);

        var result = await gate.EnsureChatbotAllowedAsync(TenantId, MembershipId, 1, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.ChatbotNotAvailableForCurrentPlan", result.Error.Code);
    }

    [Fact]
    public async Task EnsureOcrAllowedAsync_ReturnsDecision_WhenWorkspaceAndMemberQuotasAllowRequest()
    {
        var gate = CreateGate(
            planTier: PlanTier.Pro,
            tenantOcrUsed: 12,
            memberOcrUsed: 8);

        var result = await gate.EnsureOcrAllowedAsync(TenantId, MembershipId, 5, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantId, result.Value.TenantId);
        Assert.Equal(MembershipId, result.Value.MembershipId);
        Assert.Equal(PeriodStart, result.Value.PeriodStart);
        Assert.Equal(PeriodEnd, result.Value.PeriodEnd);
        Assert.Equal(SubscriptionFeature.DocumentsOcr, result.Value.Feature);
        Assert.Equal(5, result.Value.ApprovedUnitCount);
        Assert.Equal(12, result.Value.WorkspaceOcrUsed);
        Assert.Equal(8, result.Value.MemberOcrUsed);
        Assert.Equal(1_000, result.Value.Entitlements.WorkspaceMonthlyOcrPages);
        Assert.Equal(100, result.Value.Entitlements.MemberMonthlyOcrPages);
    }

    [Fact]
    public async Task RecordChatbotUsageAsync_RecordsWorkspaceAndMemberUsage()
    {
        var tenantUsageService = new RecordingTenantUsageService();
        var memberUsageService = new RecordingMemberUsageService();
        var gate = CreateGate(
            planTier: PlanTier.Pro,
            tenantUsageService: tenantUsageService,
            memberUsageService: memberUsageService);
        var decision = CreateDecision(SubscriptionFeature.Chatbot, 3, tenantChatUsed: 4, memberChatUsed: 2);

        await gate.RecordChatbotUsageAsync(decision, CancellationToken.None);

        Assert.Equal((TenantId, 3, PeriodStart, PeriodEnd), tenantUsageService.ChatbotRecord);
        Assert.Equal((TenantId, MembershipId, 3, PeriodStart, PeriodEnd), memberUsageService.ChatbotRecord);
    }

    [Fact]
    public async Task RecordChatbotUsageAsync_PropagatesTenantUsageServiceFailure()
    {
        var gate = CreateGate(
            planTier: PlanTier.Pro,
            tenantUsageService: new ThrowingTenantUsageService(new InvalidOperationException("tenant usage write failed")),
            memberUsageService: new RecordingMemberUsageService());
        var decision = CreateDecision(SubscriptionFeature.Chatbot, 2);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.RecordChatbotUsageAsync(decision, CancellationToken.None));

        Assert.Equal("tenant usage write failed", exception.Message);
    }

    [Fact]
    public async Task RecordOcrUsageAsync_RecordsWorkspaceAndMemberUsage()
    {
        var tenantUsageService = new RecordingTenantUsageService();
        var memberUsageService = new RecordingMemberUsageService();
        var gate = CreateGate(
            planTier: PlanTier.Pro,
            tenantUsageService: tenantUsageService,
            memberUsageService: memberUsageService);
        var decision = CreateDecision(SubscriptionFeature.DocumentsOcr, 9, tenantOcrUsed: 7, memberOcrUsed: 3);

        await gate.RecordOcrUsageAsync(decision, CancellationToken.None);

        Assert.Equal((TenantId, 9, PeriodStart, PeriodEnd), tenantUsageService.OcrRecord);
        Assert.Equal((TenantId, MembershipId, 9, PeriodStart, PeriodEnd), memberUsageService.OcrRecord);
    }

    private static SubscriptionQuotaDecision CreateDecision(
        SubscriptionFeature feature,
        int approvedUnitCount,
        int tenantOcrUsed = 0,
        int memberOcrUsed = 0,
        int tenantChatUsed = 0,
        int memberChatUsed = 0)
        => new(
            TenantId,
            MembershipId,
            PeriodStart,
            PeriodEnd,
            feature,
            approvedUnitCount,
            new PlanEntitlements(true, true, true, 10L * 1024 * 1024 * 1024, 1_000, 100, 10_000, 500),
            tenantOcrUsed,
            memberOcrUsed,
            tenantChatUsed,
            memberChatUsed);

    private static SubscriptionQuotaGate CreateGate(
        PlanTier planTier,
        SubscriptionStatus status = SubscriptionStatus.Active,
        int tenantOcrUsed = 0,
        int memberOcrUsed = 0,
        int tenantChatUsed = 0,
        int memberChatUsed = 0,
        ITenantUsageService? tenantUsageService = null,
        IMemberUsageService? memberUsageService = null)
    {
        var subscriptionResult = TenantSubscription.Create(
            TenantId,
            planTier,
            SubscriptionPeriodStart,
            SubscriptionPeriodEnd);

        Assert.True(subscriptionResult.IsSuccess, subscriptionResult.Error.Description);

        var subscription = subscriptionResult.Value;
        if (status != SubscriptionStatus.Active)
        {
            SetSubscriptionStatus(subscription, status);
        }

        tenantUsageService ??= CreateTenantUsageService(tenantOcrUsed, tenantChatUsed);
        memberUsageService ??= CreateMemberUsageService(memberOcrUsed, memberChatUsed);

        return CreateGateWithSubscription(subscription, tenantUsageService, memberUsageService);
    }

    private static ITenantUsageService CreateTenantUsageService(int ocrUsed, int chatbotUsed)
    {
        var usageResult = TenantUsageSnapshot.Create(TenantId, PeriodStart, PeriodEnd);
        Assert.True(usageResult.IsSuccess, usageResult.Error.Description);

        var usage = usageResult.Value;
        if (ocrUsed > 0)
        {
            var result = usage.RecordOcrUsage(ocrUsed);
            Assert.True(result.IsSuccess, result.Error.Description);
        }

        if (chatbotUsed > 0)
        {
            var result = usage.RecordChatbotUsage(chatbotUsed);
            Assert.True(result.IsSuccess, result.Error.Description);
        }

        return new StubTenantUsageService(usage);
    }

    private static IMemberUsageService CreateMemberUsageService(int ocrUsed, int chatbotUsed)
    {
        var usageResult = MemberUsageSnapshot.Create(TenantId, MembershipId, PeriodStart, PeriodEnd);
        Assert.True(usageResult.IsSuccess, usageResult.Error.Description);

        var usage = usageResult.Value;
        if (ocrUsed > 0)
        {
            var result = usage.RecordOcrUsage(ocrUsed);
            Assert.True(result.IsSuccess, result.Error.Description);
        }

        if (chatbotUsed > 0)
        {
            var result = usage.RecordChatbotUsage(chatbotUsed);
            Assert.True(result.IsSuccess, result.Error.Description);
        }

        return new StubMemberUsageService(usage);
    }

    private static SubscriptionQuotaGate CreateGateWithSubscription(
        TenantSubscription? subscription,
        ITenantUsageService tenantUsageService,
        IMemberUsageService memberUsageService)
        => new(
            new StubTenantSubscriptionRepository(subscription),
            tenantUsageService,
            memberUsageService,
            new PlanEntitlementCatalog(),
            NullLogger<SubscriptionQuotaGate>.Instance);

    private static void SetSubscriptionStatus(TenantSubscription subscription, SubscriptionStatus status)
    {
        var property = typeof(TenantSubscription).GetProperty(nameof(TenantSubscription.Status));
        Assert.NotNull(property);
        property.SetValue(subscription, status);
    }

    private sealed class StubTenantSubscriptionRepository : ITenantSubscriptionRepository
    {
        private readonly TenantSubscription? _subscription;

        public StubTenantSubscriptionRepository(TenantSubscription? subscription)
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
        private readonly TenantUsageSnapshot _usage;

        public StubTenantUsageService(TenantUsageSnapshot usage)
        {
            _usage = usage;
        }

        public Task<TenantUsageSnapshot> GetCurrentUsageAsync(
            Guid tenantId,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_usage);

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

        public Task RecordChatbotTokensAsync(
            Guid tenantId,
            long tokensUsed,
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

    private sealed class StubMemberUsageService : IMemberUsageService
    {
        private readonly MemberUsageSnapshot _usage;

        public StubMemberUsageService(MemberUsageSnapshot usage)
        {
            _usage = usage;
        }

        public Task<MemberUsageSnapshot> GetCurrentUsageAsync(
            Guid tenantId,
            Guid membershipId,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_usage);

        public Task RecordOcrUsageAsync(
            Guid tenantId,
            Guid membershipId,
            int pageCount,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordChatbotUsageAsync(
            Guid tenantId,
            Guid membershipId,
            int messageCount,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordChatbotTokensAsync(
            Guid tenantId,
            Guid membershipId,
            long tokensUsed,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingTenantUsageService : ITenantUsageService
    {
        public (Guid TenantId, int MessageCount, DateOnly PeriodStart, DateOnly PeriodEnd)? ChatbotRecord { get; private set; }
        public (Guid TenantId, int PageCount, DateOnly PeriodStart, DateOnly PeriodEnd)? OcrRecord { get; private set; }

        public Task<TenantUsageSnapshot> GetCurrentUsageAsync(
            Guid tenantId,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
        {
            var usageResult = TenantUsageSnapshot.Create(tenantId, periodStart, periodEnd);
            if (usageResult.IsFailure)
                throw new InvalidOperationException(usageResult.Error.Description);

            return Task.FromResult(usageResult.Value);
        }

        public Task RecordOcrUsageAsync(
            Guid tenantId,
            int pageCount,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
        {
            OcrRecord = (tenantId, pageCount, periodStart, periodEnd);
            return Task.CompletedTask;
        }

        public Task RecordChatbotUsageAsync(
            Guid tenantId,
            int messageCount,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
        {
            ChatbotRecord = (tenantId, messageCount, periodStart, periodEnd);
            return Task.CompletedTask;
        }

        public Task RecordChatbotTokensAsync(
            Guid tenantId,
            long tokensUsed,
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

    private sealed class ThrowingTenantUsageService : ITenantUsageService
    {
        private readonly Exception _exception;

        public ThrowingTenantUsageService(Exception exception)
        {
            _exception = exception;
        }

        public Task<TenantUsageSnapshot> GetCurrentUsageAsync(
            Guid tenantId,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
        {
            var usageResult = TenantUsageSnapshot.Create(tenantId, periodStart, periodEnd);
            if (usageResult.IsFailure)
                throw new InvalidOperationException(usageResult.Error.Description);

            return Task.FromResult(usageResult.Value);
        }

        public Task RecordOcrUsageAsync(
            Guid tenantId,
            int pageCount,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
            => Task.FromException(_exception);

        public Task RecordChatbotUsageAsync(
            Guid tenantId,
            int messageCount,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
            => Task.FromException(_exception);

        public Task RecordChatbotTokensAsync(
            Guid tenantId,
            long tokensUsed,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
            => Task.FromException(_exception);

        public Task SetStorageUsedBytesAsync(
            Guid tenantId,
            long storageUsedBytes,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingMemberUsageService : IMemberUsageService
    {
        public (Guid TenantId, Guid MembershipId, int MessageCount, DateOnly PeriodStart, DateOnly PeriodEnd)? ChatbotRecord { get; private set; }
        public (Guid TenantId, Guid MembershipId, int PageCount, DateOnly PeriodStart, DateOnly PeriodEnd)? OcrRecord { get; private set; }

        public Task<MemberUsageSnapshot> GetCurrentUsageAsync(
            Guid tenantId,
            Guid membershipId,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
        {
            var usageResult = MemberUsageSnapshot.Create(tenantId, membershipId, periodStart, periodEnd);
            if (usageResult.IsFailure)
                throw new InvalidOperationException(usageResult.Error.Description);

            return Task.FromResult(usageResult.Value);
        }

        public Task RecordOcrUsageAsync(
            Guid tenantId,
            Guid membershipId,
            int pageCount,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
        {
            OcrRecord = (tenantId, membershipId, pageCount, periodStart, periodEnd);
            return Task.CompletedTask;
        }

        public Task RecordChatbotUsageAsync(
            Guid tenantId,
            Guid membershipId,
            int messageCount,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
        {
            ChatbotRecord = (tenantId, membershipId, messageCount, periodStart, periodEnd);
            return Task.CompletedTask;
        }

        public Task RecordChatbotTokensAsync(
            Guid tenantId,
            Guid membershipId,
            long tokensUsed,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
