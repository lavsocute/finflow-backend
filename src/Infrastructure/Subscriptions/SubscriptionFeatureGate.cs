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

    private static PlanTier GetEffectivePlanTier(TenantSubscription? subscription)
    {
        if (subscription is null) return PlanTier.Free;

        // Use lazy effective status — degrades to Free when not Active.
        var effectiveStatus = subscription.ComputeEffectiveStatus(DateTime.UtcNow);
        return effectiveStatus == SubscriptionStatus.Active
            ? subscription.PlanTier
            : PlanTier.Free;
    }
}
