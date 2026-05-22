using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Enums;

namespace FinFlow.Application.Chat.Services;

public sealed class ChatPolicyEngine : IChatPolicyEngine
{
    private const string DenyMessage = "Tôi không thể hỗ trợ yêu cầu này vì quyền hiện tại chỉ cho phép xem dữ liệu trong phạm vi được phép của bạn.";
    private const string ProgrammingDenyMessage = "Tôi không hỗ trợ tạo mã nguồn, script, SQL, hay ví dụ lập trình trong chế độ trợ lý tài liệu nội bộ này.";
    private const string SensitiveAdviceDenyMessage = "Tôi không hỗ trợ đưa ra quyết định duyệt, đánh giá gian lận/compliance, hay khuyến nghị hợp tác chỉ từ nội dung tài liệu trong chatbot này.";
    private const string ClarifyManagerMessage = "Bạn muốn xem trong phạm vi phòng ban của bạn hay toàn công ty?";
    private const string ClarifyGenericMessage = "Bạn muốn xem trong phạm vi nào: của bạn, phòng ban của bạn, hay toàn công ty?";

    public ChatPolicyDecision Decide(
        ChatAuthorizationProfile profile,
        ChatIntentClassification classification,
        string query)
    {
        return classification.Family switch
        {
            ChatIntentFamily.Greeting => new ChatPolicyDecision(ChatPolicyDecisionKind.ExecuteRag),
            ChatIntentFamily.SmallTalk or ChatIntentFamily.Productivity or ChatIntentFamily.LowSignal => new ChatPolicyDecision(ChatPolicyDecisionKind.ExecuteRag),
            ChatIntentFamily.Programming => new ChatPolicyDecision(ChatPolicyDecisionKind.Deny, ProgrammingDenyMessage),
            ChatIntentFamily.SensitiveAdvice => new ChatPolicyDecision(ChatPolicyDecisionKind.Deny, SensitiveAdviceDenyMessage),
            ChatIntentFamily.DocumentLookup or ChatIntentFamily.OwnDetail => new ChatPolicyDecision(ChatPolicyDecisionKind.ExecuteRag),
            ChatIntentFamily.OwnSummary or ChatIntentFamily.ApprovalQueue => new ChatPolicyDecision(ChatPolicyDecisionKind.ExecuteReporting),
            ChatIntentFamily.Aggregate => DecideAggregate(profile, classification),
            ChatIntentFamily.Comparison or ChatIntentFamily.Ranking => DecideSensitiveAggregate(profile, classification),
            _ => classification.Mode == ChatExecutionMode.Rag
                ? new ChatPolicyDecision(ChatPolicyDecisionKind.ExecuteRag)
                : new ChatPolicyDecision(ChatPolicyDecisionKind.ExecuteReporting)
        };
    }

    private static ChatPolicyDecision DecideAggregate(ChatAuthorizationProfile profile, ChatIntentClassification classification)
    {
        // FIX #1: Block Staff at ALL confidence levels for aggregate queries (not just Ambiguous/Forbidden)
        // SafeInferred was bypassing the denial check - Staff should never access department/tenant aggregates
        if (profile.Role == RoleType.Staff)
            return new ChatPolicyDecision(ChatPolicyDecisionKind.Deny, DenyMessage);

        if (classification.ScopeConfidence == ChatScopeConfidence.Ambiguous &&
            (profile.Capabilities.CanViewDepartmentExpenseSummary || profile.Capabilities.CanViewTenantExpenseSummary))
            return new ChatPolicyDecision(ChatPolicyDecisionKind.Clarify, BuildClarifyMessage(profile));

        return new ChatPolicyDecision(ChatPolicyDecisionKind.ExecuteReporting);
    }

    private static ChatPolicyDecision DecideSensitiveAggregate(ChatAuthorizationProfile profile, ChatIntentClassification classification)
    {
        if (profile.Role == RoleType.Staff)
            return new ChatPolicyDecision(ChatPolicyDecisionKind.Deny, DenyMessage);

        if (classification.ScopeConfidence is ChatScopeConfidence.Ambiguous or ChatScopeConfidence.Forbidden)
            return new ChatPolicyDecision(ChatPolicyDecisionKind.Clarify, BuildClarifyMessage(profile));

        return new ChatPolicyDecision(ChatPolicyDecisionKind.ExecuteReporting);
    }

    private static string BuildClarifyMessage(ChatAuthorizationProfile profile) =>
        profile.Role is RoleType.Manager && !profile.Capabilities.CanViewTenantExpenseSummary
            ? ClarifyManagerMessage
            : ClarifyGenericMessage;
}
