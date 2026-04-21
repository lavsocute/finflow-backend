using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Subscriptions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.TenantSubscriptions;

namespace FinFlow.Infrastructure.Subscriptions;

public sealed class SubscriptionFeatureGate : ISubscriptionFeatureGate
{
    private readonly ITenantSubscriptionRepository _tenantSubscriptionRepository;
    private readonly ITenantUsageService _tenantUsageService;
    private readonly PlanEntitlementCatalog _planEntitlementCatalog;

    public SubscriptionFeatureGate(
        ITenantSubscriptionRepository tenantSubscriptionRepository,
        ITenantUsageService tenantUsageService,
        PlanEntitlementCatalog planEntitlementCatalog)
    {
        _tenantSubscriptionRepository = tenantSubscriptionRepository;
        _tenantUsageService = tenantUsageService;
        _planEntitlementCatalog = planEntitlementCatalog;
    }

    public async Task<PlanEntitlements> GetEntitlementsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var subscription = await _tenantSubscriptionRepository.GetByTenantIdAsync(tenantId, cancellationToken);
        return _planEntitlementCatalog.GetFor(GetEffectivePlanTier(subscription));
    }

    public async Task<Result> EnsureFeatureEnabledAsync(Guid tenantId, SubscriptionFeature feature, CancellationToken cancellationToken)
    {
        var entitlements = await GetEntitlementsAsync(tenantId, cancellationToken);

        return feature switch
        {
            SubscriptionFeature.DocumentsOcr or SubscriptionFeature.DocumentOcr =>
                entitlements.DocumentsOcrEnabled
                    ? Result.Success()
                    : Result.Failure(UploadedDocumentDraftErrors.OcrNotAvailableForCurrentPlan),
            SubscriptionFeature.DocumentUpload or SubscriptionFeature.DocumentReview or SubscriptionFeature.DocumentsManualEntry =>
                entitlements.DocumentsManualEntryEnabled
                    ? Result.Success()
                    : Result.Failure(new Error("Subscription.FeatureNotAvailable", "The current plan does not include this feature.")),
            SubscriptionFeature.Chatbot =>
                entitlements.ChatbotEnabled
                    ? Result.Success()
                    : Result.Failure(new Error("Subscription.ChatbotNotAvailableForCurrentPlan", "Chatbot is not available for the current plan.")),
            SubscriptionFeature.Storage =>
                Result.Success(),
            _ => Result.Failure(new Error("Subscription.FeatureNotAvailable", "The current plan does not include this feature."))
        };
    }

    public async Task<Result> EnsureOcrAllowedAsync(Guid tenantId, int pageCount, CancellationToken cancellationToken)
    {
        if (pageCount <= 0)
            return Result.Failure(new Error("Subscription.OcrPageCountInvalid", "OCR page count must be greater than zero."));

        var featureResult = await EnsureFeatureEnabledAsync(tenantId, SubscriptionFeature.DocumentsOcr, cancellationToken);
        if (featureResult.IsFailure)
            return featureResult;

        var subscription = await _tenantSubscriptionRepository.GetByTenantIdAsync(tenantId, cancellationToken);
        if (subscription is null || subscription.Status != SubscriptionStatus.Active)
            return Result.Failure(UploadedDocumentDraftErrors.OcrNotAvailableForCurrentPlan);

        var entitlements = _planEntitlementCatalog.GetFor(subscription.PlanTier);
        var usage = await _tenantUsageService.GetCurrentUsageAsync(
            tenantId,
            DateOnly.FromDateTime(subscription.PeriodStart),
            DateOnly.FromDateTime(subscription.PeriodEnd),
            cancellationToken);

        return usage.OcrPagesUsed + pageCount > entitlements.MonthlyOcrPages
            ? Result.Failure(new Error("Subscription.OcrQuotaExceeded", "The current plan has reached its monthly OCR quota."))
            : Result.Success();
    }

    public async Task<Result> EnsureChatbotAllowedAsync(Guid tenantId, int messageCount, CancellationToken cancellationToken)
    {
        if (messageCount <= 0)
            return Result.Failure(new Error("Subscription.ChatbotMessageCountInvalid", "Chatbot message count must be greater than zero."));

        var featureResult = await EnsureFeatureEnabledAsync(tenantId, SubscriptionFeature.Chatbot, cancellationToken);
        if (featureResult.IsFailure)
            return featureResult;

        var subscription = await _tenantSubscriptionRepository.GetByTenantIdAsync(tenantId, cancellationToken);
        if (subscription is null || subscription.Status != SubscriptionStatus.Active)
            return Result.Failure(new Error("Subscription.ChatbotNotAvailableForCurrentPlan", "Chatbot is not available for the current plan."));

        var entitlements = _planEntitlementCatalog.GetFor(subscription.PlanTier);
        var usage = await _tenantUsageService.GetCurrentUsageAsync(
            tenantId,
            DateOnly.FromDateTime(subscription.PeriodStart),
            DateOnly.FromDateTime(subscription.PeriodEnd),
            cancellationToken);

        return usage.ChatbotMessagesUsed + messageCount > entitlements.MonthlyChatbotMessages
            ? Result.Failure(new Error("Subscription.ChatbotQuotaExceeded", "The current plan has reached its monthly chatbot quota."))
            : Result.Success();
    }

    private static PlanTier GetEffectivePlanTier(TenantSubscription? subscription) =>
        subscription is not null && subscription.Status == SubscriptionStatus.Active
            ? subscription.PlanTier
            : PlanTier.Free;
}
