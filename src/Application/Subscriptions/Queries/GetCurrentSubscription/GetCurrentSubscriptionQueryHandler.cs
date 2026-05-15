using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Subscriptions.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantSubscriptions;
using MediatR;

namespace FinFlow.Application.Subscriptions.Queries.GetCurrentSubscription;

public sealed class GetCurrentSubscriptionQueryHandler : IRequestHandler<GetCurrentSubscriptionQuery, Result<CurrentSubscriptionResponse>>
{
    private static readonly Error MembershipRequiredError = new(
        "Subscription.MembershipRequired",
        "The current membership is required to load member subscription usage.");

    private readonly ITenantSubscriptionRepository _tenantSubscriptionRepository;
    private readonly ISubscriptionFeatureGate _subscriptionFeatureGate;
    private readonly ITenantUsageService _tenantUsageService;
    private readonly IMemberUsageService _memberUsageService;
    private readonly ICurrentTenant _currentTenant;

    public GetCurrentSubscriptionQueryHandler(
        ITenantSubscriptionRepository tenantSubscriptionRepository,
        ISubscriptionFeatureGate subscriptionFeatureGate,
        ITenantUsageService tenantUsageService,
        IMemberUsageService memberUsageService,
        ICurrentTenant currentTenant)
    {
        _tenantSubscriptionRepository = tenantSubscriptionRepository;
        _subscriptionFeatureGate = subscriptionFeatureGate;
        _tenantUsageService = tenantUsageService;
        _memberUsageService = memberUsageService;
        _currentTenant = currentTenant;
    }

    public async Task<Result<CurrentSubscriptionResponse>> Handle(GetCurrentSubscriptionQuery request, CancellationToken cancellationToken)
    {
        if (!_currentTenant.MembershipId.HasValue || _currentTenant.MembershipId.Value == Guid.Empty)
            return Result.Failure<CurrentSubscriptionResponse>(MembershipRequiredError);

        var subscription = await _tenantSubscriptionRepository.GetByTenantIdAsync(request.TenantId, cancellationToken);
        if (subscription is null)
        {
            var fallbackSubscription = CreateFallbackSubscription(request.TenantId);
            return Result.Success(await BuildResponseAsync(fallbackSubscription, _currentTenant.MembershipId.Value, cancellationToken));
        }

        return Result.Success(await BuildResponseAsync(subscription, _currentTenant.MembershipId.Value, cancellationToken));
    }

    private async Task<CurrentSubscriptionResponse> BuildResponseAsync(
        TenantSubscription subscription,
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        var periodStart = DateOnly.FromDateTime(subscription.PeriodStart);
        var periodEnd = DateOnly.FromDateTime(subscription.PeriodEnd);
        var entitlements = await _subscriptionFeatureGate.GetEntitlementsAsync(subscription.IdTenant, cancellationToken);
        var usage = await _tenantUsageService.GetCurrentUsageAsync(
            subscription.IdTenant,
            periodStart,
            periodEnd,
            cancellationToken);
        var memberUsage = await _memberUsageService.GetCurrentUsageAsync(
            subscription.IdTenant,
            membershipId,
            periodStart,
            periodEnd,
            cancellationToken);

        return new CurrentSubscriptionResponse(
            subscription.PlanTier.ToString(),
            subscription.Status.ToString(),
            subscription.PeriodStart,
            subscription.PeriodEnd,
            new CurrentSubscriptionEntitlementsResponse(
                entitlements.DocumentsManualEntryEnabled,
                entitlements.DocumentsOcrEnabled,
                entitlements.ChatbotEnabled,
                entitlements.StorageLimitBytes,
                entitlements.WorkspaceMonthlyOcrPages,
                entitlements.MemberMonthlyOcrPages,
                entitlements.WorkspaceMonthlyChatbotMessages,
                entitlements.MemberMonthlyChatbotMessages),
            new CurrentSubscriptionUsageResponse(
                usage.OcrPagesUsed,
                usage.ChatbotMessagesUsed,
                usage.StorageUsedBytes),
            new CurrentSubscriptionMemberUsageResponse(
                memberUsage.OcrPagesUsed,
                memberUsage.ChatbotMessagesUsed,
                Math.Max(0, entitlements.MemberMonthlyOcrPages - memberUsage.OcrPagesUsed),
                Math.Max(0, entitlements.MemberMonthlyChatbotMessages - memberUsage.ChatbotMessagesUsed)));
    }

    private static TenantSubscription CreateFallbackSubscription(Guid tenantId)
    {
        var nowUtc = DateTime.UtcNow;
        var periodStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1);

        var result = TenantSubscription.Create(tenantId, PlanTier.Free, periodStart, periodEnd);
        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Description);

        return result.Value;
    }
}
