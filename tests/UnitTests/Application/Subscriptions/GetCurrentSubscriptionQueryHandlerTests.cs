using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Subscriptions;
using FinFlow.Application.Subscriptions.DTOs.Responses;
using FinFlow.Application.Subscriptions.Queries.GetCurrentSubscription;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantSubscriptions;
using FinFlow.Domain.TenantUsageSnapshots;
using FinFlow.Infrastructure.Subscriptions;

namespace FinFlow.UnitTests.Application.Subscriptions;

public sealed class GetCurrentSubscriptionQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MembershipId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Handle_ReturnsCurrentSubscription_WithWorkspaceAndMemberEntitlementsAndUsage()
    {
        var periodStart = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-1), DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1);

        var subscriptionResult = TenantSubscription.Create(TenantId, PlanTier.Pro, periodStart, periodEnd);
        Assert.True(subscriptionResult.IsSuccess, subscriptionResult.Error.Description);

        var usageResult = TenantUsageSnapshot.Create(TenantId, DateOnly.FromDateTime(periodStart), DateOnly.FromDateTime(periodEnd));
        Assert.True(usageResult.IsSuccess, usageResult.Error.Description);

        Assert.True(usageResult.Value.RecordOcrUsage(7).IsSuccess);
        Assert.True(usageResult.Value.RecordChatbotUsage(11).IsSuccess);
        Assert.True(usageResult.Value.SetStorageUsedBytes(1_234).IsSuccess);

        var memberUsageResult = MemberUsageSnapshot.Create(TenantId, MembershipId, DateOnly.FromDateTime(periodStart), DateOnly.FromDateTime(periodEnd));
        Assert.True(memberUsageResult.IsSuccess, memberUsageResult.Error.Description);
        Assert.True(memberUsageResult.Value.RecordOcrUsage(12).IsSuccess);
        Assert.True(memberUsageResult.Value.RecordChatbotUsage(12).IsSuccess);

        var usageService = new RecordingTenantUsageService(usageResult.Value);
        var memberUsageService = new RecordingMemberUsageService(memberUsageResult.Value);
        var subscriptionRepository = new StubTenantSubscriptionRepository(subscriptionResult.Value);
        var handler = CreateHandler(subscriptionRepository, usageService, memberUsageService, MembershipId);

        var result = await handler.Handle(new GetCurrentSubscriptionQuery(TenantId), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal("Pro", result.Value.PlanTier);
        Assert.Equal("Active", result.Value.Status);
        Assert.Equal(periodStart, result.Value.CurrentPeriodStart);
        Assert.Equal(periodEnd, result.Value.CurrentPeriodEnd);
        Assert.True(result.Value.Entitlements.DocumentsOcrEnabled);
        Assert.True(result.Value.Entitlements.ChatbotEnabled);
        Assert.Equal(1_000, result.Value.Entitlements.WorkspaceMonthlyOcrPages);
        Assert.Equal(100, result.Value.Entitlements.MemberMonthlyOcrPages);
        Assert.Equal(10_000, result.Value.Entitlements.WorkspaceMonthlyChatbotMessages);
        Assert.Equal(500, result.Value.Entitlements.MemberMonthlyChatbotMessages);
        Assert.Equal(7, result.Value.Usage.OcrPagesUsed);
        Assert.Equal(11, result.Value.Usage.ChatbotMessagesUsed);
        Assert.Equal(1_234, result.Value.Usage.StorageUsedBytes);
        Assert.Equal(12, result.Value.CurrentMemberUsage.OcrPagesUsed);
        Assert.Equal(12, result.Value.CurrentMemberUsage.ChatbotMessagesUsed);
        Assert.Equal(88, result.Value.CurrentMemberUsage.RemainingOcrPages);
        Assert.Equal(488, result.Value.CurrentMemberUsage.RemainingChatbotMessages);
        Assert.Equal(TenantId, memberUsageService.LastTenantId);
        Assert.Equal(MembershipId, memberUsageService.LastMembershipId);
    }

    [Fact]
    public async Task Handle_FallsBackToFreePlan_WhenSubscriptionIsMissing()
    {
        var now = DateTime.UtcNow;
        var expectedStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var expectedEnd = expectedStart.AddMonths(1);

        var usageService = new RecordingTenantUsageService();
        var memberUsageService = new RecordingMemberUsageService();
        var subscriptionRepository = new StubTenantSubscriptionRepository();
        var handler = CreateHandler(subscriptionRepository, usageService, memberUsageService, MembershipId);

        var result = await handler.Handle(new GetCurrentSubscriptionQuery(TenantId), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal("Free", result.Value.PlanTier);
        Assert.Equal("Active", result.Value.Status);
        Assert.False(result.Value.Entitlements.DocumentsOcrEnabled);
        Assert.False(result.Value.Entitlements.ChatbotEnabled);
        Assert.Equal(DateOnly.FromDateTime(expectedStart), usageService.LastPeriodStart);
        Assert.Equal(DateOnly.FromDateTime(expectedEnd), usageService.LastPeriodEnd);
        Assert.Equal(DateOnly.FromDateTime(expectedStart), memberUsageService.LastPeriodStart);
        Assert.Equal(DateOnly.FromDateTime(expectedEnd), memberUsageService.LastPeriodEnd);
        Assert.Equal(0, result.Value.Usage.OcrPagesUsed);
        Assert.Equal(0, result.Value.CurrentMemberUsage.OcrPagesUsed);
        Assert.Equal(0, result.Value.CurrentMemberUsage.ChatbotMessagesUsed);
        Assert.Equal(0, result.Value.CurrentMemberUsage.RemainingOcrPages);
        Assert.Equal(0, result.Value.CurrentMemberUsage.RemainingChatbotMessages);
    }

    [Fact]
    public async Task Handle_ReturnsCanceledSubscriptionStatus_WhenSubscriptionWasCanceled()
    {
        var periodStart = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-1), DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1);

        var subscriptionResult = TenantSubscription.Create(TenantId, PlanTier.Pro, periodStart, periodEnd);
        Assert.True(subscriptionResult.IsSuccess, subscriptionResult.Error.Description);
        Assert.True(subscriptionResult.Value.Cancel().IsSuccess);

        var usageService = new RecordingTenantUsageService();
        var memberUsageService = new RecordingMemberUsageService();
        var subscriptionRepository = new StubTenantSubscriptionRepository(subscriptionResult.Value);
        var handler = CreateHandler(subscriptionRepository, usageService, memberUsageService, MembershipId);

        var result = await handler.Handle(new GetCurrentSubscriptionQuery(TenantId), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal("Pro", result.Value.PlanTier);
        Assert.Equal("Canceled", result.Value.Status);
        Assert.False(result.Value.Entitlements.DocumentsOcrEnabled);
        Assert.False(result.Value.Entitlements.ChatbotEnabled);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenAuthenticatedMembershipIsMissing()
    {
        var usageService = new RecordingTenantUsageService();
        var memberUsageService = new RecordingMemberUsageService();
        var subscriptionRepository = new StubTenantSubscriptionRepository();
        var handler = CreateHandler(subscriptionRepository, usageService, memberUsageService, membershipId: null);

        var result = await handler.Handle(new GetCurrentSubscriptionQuery(TenantId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.MembershipRequired", result.Error.Code);
    }

    private static GetCurrentSubscriptionQueryHandler CreateHandler(
        ITenantSubscriptionRepository subscriptionRepository,
        RecordingTenantUsageService usageService,
        RecordingMemberUsageService memberUsageService,
        Guid? membershipId)
        => new(
            subscriptionRepository,
            new SubscriptionFeatureGate(subscriptionRepository, usageService, new PlanEntitlementCatalog()),
            usageService,
            memberUsageService,
            new StubCurrentTenant(TenantId, membershipId));

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

        public Task RecordChatbotTokensAsync(Guid tenantId, long tokensUsed, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetStorageUsedBytesAsync(Guid tenantId, long storageUsedBytes, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingMemberUsageService : IMemberUsageService
    {
        private readonly MemberUsageSnapshot? _snapshot;

        public RecordingMemberUsageService(MemberUsageSnapshot? snapshot = null)
        {
            _snapshot = snapshot;
        }

        public Guid? LastTenantId { get; private set; }
        public Guid? LastMembershipId { get; private set; }
        public DateOnly? LastPeriodStart { get; private set; }
        public DateOnly? LastPeriodEnd { get; private set; }

        public Task<MemberUsageSnapshot> GetCurrentUsageAsync(
            Guid tenantId,
            Guid membershipId,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
        {
            LastTenantId = tenantId;
            LastMembershipId = membershipId;
            LastPeriodStart = periodStart;
            LastPeriodEnd = periodEnd;

            if (_snapshot is not null)
                return Task.FromResult(_snapshot);

            var createResult = MemberUsageSnapshot.Create(tenantId, membershipId, periodStart, periodEnd);
            Assert.True(createResult.IsSuccess, createResult.Error.Description);
            return Task.FromResult(createResult.Value);
        }

        public Task RecordOcrUsageAsync(Guid tenantId, Guid membershipId, int pageCount, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordChatbotUsageAsync(Guid tenantId, Guid membershipId, int messageCount, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordChatbotTokensAsync(Guid tenantId, Guid membershipId, long tokensUsed, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public StubCurrentTenant(Guid? id, Guid? membershipId)
        {
            Id = id;
            MembershipId = membershipId;
        }

        public Guid? Id { get; set; }
        public Guid? MembershipId { get; set; }
        public bool IsAvailable => Id.HasValue;
        public bool IsSuperAdmin { get; set; }

        public IDisposable BeginScope(Guid? tenantId, Guid? membershipId = null, bool isSuperAdmin = false)
            => NoOpDisposable.Instance;
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new();
        public void Dispose() { }
    }
}
