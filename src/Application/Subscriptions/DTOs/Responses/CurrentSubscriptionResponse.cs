namespace FinFlow.Application.Subscriptions.DTOs.Responses;

public sealed record CurrentSubscriptionResponse(
    string PlanTier,
    string Status,
    DateTime CurrentPeriodStart,
    DateTime CurrentPeriodEnd,
    CurrentSubscriptionEntitlementsResponse Entitlements,
    CurrentSubscriptionUsageResponse Usage,
    CurrentSubscriptionMemberUsageResponse CurrentMemberUsage);

public sealed record CurrentSubscriptionEntitlementsResponse(
    bool DocumentsManualEntryEnabled,
    bool DocumentsOcrEnabled,
    bool ChatbotEnabled,
    long StorageLimitBytes,
    int WorkspaceMonthlyOcrPages,
    int MemberMonthlyOcrPages,
    int WorkspaceMonthlyChatbotMessages,
    int MemberMonthlyChatbotMessages);

public sealed record CurrentSubscriptionUsageResponse(
    int OcrPagesUsed,
    int ChatbotMessagesUsed,
    long StorageUsedBytes);

public sealed record CurrentSubscriptionMemberUsageResponse(
    int OcrPagesUsed,
    int ChatbotMessagesUsed,
    int RemainingOcrPages,
    int RemainingChatbotMessages);
