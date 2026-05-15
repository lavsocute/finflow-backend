using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Subscriptions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.TenantSubscriptions;
using Microsoft.Extensions.Logging;

namespace FinFlow.Infrastructure.Subscriptions;

public sealed class SubscriptionQuotaGate : ISubscriptionQuotaGate
{
    private static readonly Error OcrPageCountInvalid = new("Subscription.OcrPageCountInvalid", "OCR page count must be greater than zero.");
    private static readonly Error ChatbotMessageCountInvalid = new("Subscription.ChatbotMessageCountInvalid", "Chatbot message count must be greater than zero.");
    private static readonly Error OcrQuotaExceeded = new("Subscription.OcrQuotaExceeded", "The current plan has reached its monthly OCR quota.");
    private static readonly Error OcrMemberQuotaExceeded = new("Subscription.OcrMemberQuotaExceeded", "The current member has reached the monthly OCR quota.");
    private static readonly Error ChatbotQuotaExceeded = new("Subscription.ChatbotQuotaExceeded", "The current plan has reached its monthly chatbot quota.");
    private static readonly Error ChatbotMemberQuotaExceeded = new("Subscription.ChatbotMemberQuotaExceeded", "The current member has reached the monthly chatbot quota.");
    private static readonly Error ChatbotNotAvailableForCurrentPlan = new("Subscription.ChatbotNotAvailableForCurrentPlan", "Chatbot is not available for the current plan.");

    private readonly ITenantSubscriptionRepository _tenantSubscriptionRepository;
    private readonly ITenantUsageService _tenantUsageService;
    private readonly IMemberUsageService _memberUsageService;
    private readonly PlanEntitlementCatalog _planEntitlementCatalog;
    private readonly ILogger<SubscriptionQuotaGate> _logger;

    public SubscriptionQuotaGate(
        ITenantSubscriptionRepository tenantSubscriptionRepository,
        ITenantUsageService tenantUsageService,
        IMemberUsageService memberUsageService,
        PlanEntitlementCatalog planEntitlementCatalog,
        ILogger<SubscriptionQuotaGate> logger)
    {
        _tenantSubscriptionRepository = tenantSubscriptionRepository;
        _tenantUsageService = tenantUsageService;
        _memberUsageService = memberUsageService;
        _planEntitlementCatalog = planEntitlementCatalog;
        _logger = logger;
    }

    public Task<Result<SubscriptionQuotaDecision>> EnsureChatbotAllowedAsync(
        Guid tenantId,
        Guid membershipId,
        int messageCount,
        CancellationToken cancellationToken)
        => EnsureAllowedAsync(
            tenantId,
            membershipId,
            messageCount,
            SubscriptionFeature.Chatbot,
            x => x.ChatbotEnabled,
            x => x.WorkspaceMonthlyChatbotMessages,
            x => x.MemberMonthlyChatbotMessages,
            x => x.ChatbotMessagesUsed,
            x => x.ChatbotMessagesUsed,
            ChatbotMessageCountInvalid,
            ChatbotNotAvailableForCurrentPlan,
            ChatbotQuotaExceeded,
            ChatbotMemberQuotaExceeded,
            cancellationToken);

    public Task<Result<SubscriptionQuotaDecision>> EnsureOcrAllowedAsync(
        Guid tenantId,
        Guid membershipId,
        int pageCount,
        CancellationToken cancellationToken)
        => EnsureAllowedAsync(
            tenantId,
            membershipId,
            pageCount,
            SubscriptionFeature.DocumentsOcr,
            x => x.DocumentsOcrEnabled,
            x => x.WorkspaceMonthlyOcrPages,
            x => x.MemberMonthlyOcrPages,
            x => x.OcrPagesUsed,
            x => x.OcrPagesUsed,
            OcrPageCountInvalid,
            UploadedDocumentDraftErrors.OcrNotAvailableForCurrentPlan,
            OcrQuotaExceeded,
            OcrMemberQuotaExceeded,
            cancellationToken);

    public async Task RecordChatbotUsageAsync(
        SubscriptionQuotaDecision decision,
        CancellationToken cancellationToken)
    {
        if (decision.Feature != SubscriptionFeature.Chatbot)
            throw new InvalidOperationException($"Quota decision feature mismatch. Expected {SubscriptionFeature.Chatbot} but received {decision.Feature}.");

        await _tenantUsageService.RecordChatbotUsageAsync(
            decision.TenantId,
            decision.ApprovedUnitCount,
            decision.PeriodStart,
            decision.PeriodEnd,
            cancellationToken);

        await _memberUsageService.RecordChatbotUsageAsync(
            decision.TenantId,
            decision.MembershipId,
            decision.ApprovedUnitCount,
            decision.PeriodStart,
            decision.PeriodEnd,
            cancellationToken);
    }

    public async Task RecordOcrUsageAsync(
        SubscriptionQuotaDecision decision,
        CancellationToken cancellationToken)
    {
        if (decision.Feature is not SubscriptionFeature.DocumentsOcr and not SubscriptionFeature.DocumentOcr)
            throw new InvalidOperationException($"Quota decision feature mismatch. Expected {SubscriptionFeature.DocumentsOcr} but received {decision.Feature}.");

        await _tenantUsageService.RecordOcrUsageAsync(
            decision.TenantId,
            decision.ApprovedUnitCount,
            decision.PeriodStart,
            decision.PeriodEnd,
            cancellationToken);

        await _memberUsageService.RecordOcrUsageAsync(
            decision.TenantId,
            decision.MembershipId,
            decision.ApprovedUnitCount,
            decision.PeriodStart,
            decision.PeriodEnd,
            cancellationToken);
    }

    private async Task<Result<SubscriptionQuotaDecision>> EnsureAllowedAsync(
        Guid tenantId,
        Guid membershipId,
        int requestedUnits,
        SubscriptionFeature feature,
        Func<PlanEntitlements, bool> isFeatureEnabled,
        Func<PlanEntitlements, int> getWorkspaceLimit,
        Func<PlanEntitlements, int> getMemberLimit,
        Func<TenantUsageSnapshot, int> getWorkspaceUsed,
        Func<MemberUsageSnapshot, int> getMemberUsed,
        Error invalidRequestedUnitsError,
        Error featureUnavailableError,
        Error workspaceQuotaExceededError,
        Error memberQuotaExceededError,
        CancellationToken cancellationToken)
    {
        if (requestedUnits <= 0)
            return Result.Failure<SubscriptionQuotaDecision>(invalidRequestedUnitsError);

        var subscription = await _tenantSubscriptionRepository.GetByTenantIdAsync(tenantId, cancellationToken);
        if (subscription is null)
        {
            LogDecision(
                "SubscriptionMissing",
                feature,
                tenantId,
                membershipId,
                requestedUnits,
                null, null, null, null, null, null);
            return Result.Failure<SubscriptionQuotaDecision>(featureUnavailableError);
        }

        var now = DateTime.UtcNow;
        var effectiveStatus = subscription.ComputeEffectiveStatus(now);

        // Block all quota-consuming features when status is not Active.
        // PastDue/Expired/Canceled/Paused → no OCR, no Chat (read-only access only).
        if (effectiveStatus != SubscriptionStatus.Active)
        {
            LogDecision(
                $"SubscriptionNotActive_{effectiveStatus}",
                feature,
                tenantId,
                membershipId,
                requestedUnits,
                null, null, null, null, null, null);
            return Result.Failure<SubscriptionQuotaDecision>(featureUnavailableError);
        }

        var entitlements = _planEntitlementCatalog.GetFor(subscription.PlanTier);
        // Use lazy-computed current period (anchored to PeriodStart, advanced by BillingCycleMonths).
        var (currentPeriodStart, currentPeriodEnd) = subscription.ComputeCurrentPeriod(now);
        var periodStart = DateOnly.FromDateTime(currentPeriodStart);
        var periodEnd = DateOnly.FromDateTime(currentPeriodEnd);

        if (!isFeatureEnabled(entitlements))
        {
            LogDecision(
                "FeatureDisabled",
                feature,
                tenantId,
                membershipId,
                requestedUnits,
                0,
                getWorkspaceLimit(entitlements),
                0,
                getMemberLimit(entitlements),
                periodStart,
                periodEnd);
            return Result.Failure<SubscriptionQuotaDecision>(featureUnavailableError);
        }

        var workspaceUsage = await _tenantUsageService.GetCurrentUsageAsync(
            tenantId,
            periodStart,
            periodEnd,
            cancellationToken);
        var memberUsage = await _memberUsageService.GetCurrentUsageAsync(
            tenantId,
            membershipId,
            periodStart,
            periodEnd,
            cancellationToken);

        var workspaceUsed = getWorkspaceUsed(workspaceUsage);
        var workspaceLimit = getWorkspaceLimit(entitlements);
        var memberUsed = getMemberUsed(memberUsage);
        var memberLimit = getMemberLimit(entitlements);

        if (workspaceUsed + requestedUnits > workspaceLimit)
        {
            LogDecision(
                "WorkspaceQuotaExceeded",
                feature,
                tenantId,
                membershipId,
                requestedUnits,
                workspaceUsed,
                workspaceLimit,
                memberUsed,
                memberLimit,
                periodStart,
                periodEnd);
            return Result.Failure<SubscriptionQuotaDecision>(workspaceQuotaExceededError);
        }

        if (memberUsed + requestedUnits > memberLimit)
        {
            LogDecision(
                "MemberQuotaExceeded",
                feature,
                tenantId,
                membershipId,
                requestedUnits,
                workspaceUsed,
                workspaceLimit,
                memberUsed,
                memberLimit,
                periodStart,
                periodEnd);
            return Result.Failure<SubscriptionQuotaDecision>(memberQuotaExceededError);
        }

        var decision = new SubscriptionQuotaDecision(
            tenantId,
            membershipId,
            periodStart,
            periodEnd,
            feature,
            requestedUnits,
            entitlements,
            workspaceUsage.OcrPagesUsed,
            memberUsage.OcrPagesUsed,
            workspaceUsage.ChatbotMessagesUsed,
            memberUsage.ChatbotMessagesUsed);

        LogDecision(
            "Allowed",
            feature,
            tenantId,
            membershipId,
            requestedUnits,
            workspaceUsed,
            workspaceLimit,
            memberUsed,
            memberLimit,
            periodStart,
            periodEnd);

        return Result.Success(decision);
    }

    private void LogDecision(
        string decision,
        SubscriptionFeature feature,
        Guid tenantId,
        Guid membershipId,
        int requestedUnits,
        int? workspaceUsed,
        int? workspaceLimit,
        int? memberUsed,
        int? memberLimit,
        DateOnly? periodStart,
        DateOnly? periodEnd)
    {
        _logger.LogInformation(
            "Subscription quota decision {Decision} for {Feature}. TenantId: {TenantId}, MembershipId: {MembershipId}, RequestedUnits: {RequestedUnits}, WorkspaceUsed: {WorkspaceUsed}, WorkspaceLimit: {WorkspaceLimit}, MemberUsed: {MemberUsed}, MemberLimit: {MemberLimit}, PeriodStart: {PeriodStart}, PeriodEnd: {PeriodEnd}",
            decision,
            feature,
            tenantId,
            membershipId,
            requestedUnits,
            workspaceUsed,
            workspaceLimit,
            memberUsed,
            memberLimit,
            periodStart,
            periodEnd);
    }
}
