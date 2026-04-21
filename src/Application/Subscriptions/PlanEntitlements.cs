namespace FinFlow.Application.Subscriptions;

public sealed record PlanEntitlements(
    bool DocumentsManualEntryEnabled,
    bool DocumentsOcrEnabled,
    bool ChatbotEnabled,
    long StorageLimitBytes,
    int MonthlyOcrPages,
    int MonthlyChatbotMessages);
