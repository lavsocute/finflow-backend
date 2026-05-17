namespace FinFlow.Application.Subscriptions;

public sealed record PlanEntitlements(
    bool DocumentsManualEntryEnabled,
    bool DocumentsOcrEnabled,
    bool ChatbotEnabled,
    long StorageLimitBytes,
    int WorkspaceMonthlyOcrPages,
    int MemberMonthlyOcrPages,
    int WorkspaceMonthlyChatbotMessages,
    int MemberMonthlyChatbotMessages,
    long WorkspaceMonthlyChatbotTokens = 0L,
    long MemberMonthlyChatbotTokens = 0L)
{
    // Compatibility only: later quota-gate work should bind to the workspace/member-specific properties instead.
    [Obsolete("Compatibility alias for workspace OCR quota only. Prefer WorkspaceMonthlyOcrPages or MemberMonthlyOcrPages.", false)]
    public int MonthlyOcrPages => WorkspaceMonthlyOcrPages;

    // Compatibility only: later quota-gate work should bind to the workspace/member-specific properties instead.
    [Obsolete("Compatibility alias for workspace chatbot quota only. Prefer WorkspaceMonthlyChatbotMessages or MemberMonthlyChatbotMessages.", false)]
    public int MonthlyChatbotMessages => WorkspaceMonthlyChatbotMessages;

    // Compatibility only: preserved temporarily so existing callers continue to compile until quota-gate cleanup lands.
    [Obsolete("Compatibility constructor maps legacy monthly quotas to workspace quotas and leaves member quotas at zero.", false)]
    public PlanEntitlements(
        bool documentsManualEntryEnabled,
        bool documentsOcrEnabled,
        bool chatbotEnabled,
        long storageLimitBytes,
        int monthlyOcrPages,
        int monthlyChatbotMessages)
        : this(
            documentsManualEntryEnabled,
            documentsOcrEnabled,
            chatbotEnabled,
            storageLimitBytes,
            monthlyOcrPages,
            0,
            monthlyChatbotMessages,
            0)
    {
    }
}
