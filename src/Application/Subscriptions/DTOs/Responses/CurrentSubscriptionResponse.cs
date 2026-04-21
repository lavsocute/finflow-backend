namespace FinFlow.Application.Subscriptions.DTOs.Responses;

public sealed record CurrentSubscriptionResponse(
    string PlanTier,
    string Status,
    DateTime CurrentPeriodStart,
    DateTime CurrentPeriodEnd,
    CurrentSubscriptionEntitlementsResponse Entitlements,
    CurrentSubscriptionUsageResponse Usage);

public sealed record CurrentSubscriptionEntitlementsResponse(
    bool DocumentsManualEntryEnabled,
    bool DocumentsOcrEnabled,
    bool ChatbotEnabled,
    long StorageLimitBytes,
    int MonthlyOcrPages,
    int MonthlyChatbotMessages);

public sealed record CurrentSubscriptionUsageResponse(
    int OcrPagesUsed,
    int ChatbotMessagesUsed,
    long StorageUsedBytes);
