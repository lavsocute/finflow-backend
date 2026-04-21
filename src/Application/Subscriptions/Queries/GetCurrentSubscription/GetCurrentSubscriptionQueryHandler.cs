using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Subscriptions.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.TenantSubscriptions;
using MediatR;

namespace FinFlow.Application.Subscriptions.Queries.GetCurrentSubscription;

public sealed class GetCurrentSubscriptionQueryHandler : IRequestHandler<GetCurrentSubscriptionQuery, Result<CurrentSubscriptionResponse>>
{
    private readonly ITenantSubscriptionRepository _tenantSubscriptionRepository;
    private readonly ISubscriptionFeatureGate _subscriptionFeatureGate;
    private readonly ITenantUsageService _tenantUsageService;

    public GetCurrentSubscriptionQueryHandler(
        ITenantSubscriptionRepository tenantSubscriptionRepository,
        ISubscriptionFeatureGate subscriptionFeatureGate,
        ITenantUsageService tenantUsageService)
    {
        _tenantSubscriptionRepository = tenantSubscriptionRepository;
        _subscriptionFeatureGate = subscriptionFeatureGate;
        _tenantUsageService = tenantUsageService;
    }

    public async Task<Result<CurrentSubscriptionResponse>> Handle(GetCurrentSubscriptionQuery request, CancellationToken cancellationToken)
    {
        var subscription = await _tenantSubscriptionRepository.GetByTenantIdAsync(request.TenantId, cancellationToken);
        if (subscription is null)
        {
            var fallbackSubscription = CreateFallbackSubscription(request.TenantId);
            return Result.Success(await BuildResponseAsync(fallbackSubscription, cancellationToken));
        }

        return Result.Success(await BuildResponseAsync(subscription, cancellationToken));
    }

    private async Task<CurrentSubscriptionResponse> BuildResponseAsync(TenantSubscription subscription, CancellationToken cancellationToken)
    {
        var entitlements = await _subscriptionFeatureGate.GetEntitlementsAsync(subscription.IdTenant, cancellationToken);
        var usage = await _tenantUsageService.GetCurrentUsageAsync(
            subscription.IdTenant,
            DateOnly.FromDateTime(subscription.PeriodStart),
            DateOnly.FromDateTime(subscription.PeriodEnd),
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
                entitlements.MonthlyOcrPages,
                entitlements.MonthlyChatbotMessages),
            new CurrentSubscriptionUsageResponse(
                usage.OcrPagesUsed,
                usage.ChatbotMessagesUsed,
                usage.StorageUsedBytes));
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
