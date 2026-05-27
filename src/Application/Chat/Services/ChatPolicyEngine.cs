using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Enums;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Determines whether a chat query should be executed, denied, or requires clarification based on authorization and intent.
/// </summary>
public sealed class ChatPolicyEngine : IChatPolicyEngine
{
    private const string DenyMessage = "Tôi không thể hỗ trợ yêu cầu này vì quyền hiện tại chỉ cho phép xem dữ liệu trong phạm vi được phép của bạn.";
    private const string ProgrammingDenyMessage = "Tôi không hỗ trợ tạo mã nguồn, script, SQL, hay ví dụ lập trình trong chế độ trợ lý tài liệu nội bộ này.";
    private const string SensitiveAdviceDenyMessage = "Tôi không hỗ trợ đưa ra quyết định duyệt, đánh giá gian lận/compliance, hay khuyến nghị hợp tác chỉ từ nội dung tài liệu trong chatbot này.";
    private const string PromptBoundaryDenyMessage = "Tôi chưa thể cung cấp nội dung hướng dẫn nội bộ. Bạn có thể hỏi về chứng từ, chi phí, phê duyệt hoặc báo cáo trong workspace.";
    private const string DestructiveCommandDenyMessage = "Tôi không thể thực hiện lệnh này vì đây là hành động phá hủy dữ liệu và không được phép trong chatbot.";
    private const string WriteActionDenyMessage = "Tôi là trợ lý Q&A chỉ đọc (read-only). Tôi không thể thực hiện các thao tác ghi/điều chỉnh/xóa dữ liệu.";
    private const string AmbiguityClarificationMessage = "Bạn có thể nói rõ hơn được không? Ví dụ: \"Những ai đã duyệt chứng từ tháng này?\", \"Cho tôi xem chi phí phòng ban tuần này\", hoặc \"Ai đã duyệt phiếu chi #12345\".";

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
            ChatIntentFamily.PromptBoundary => new ChatPolicyDecision(ChatPolicyDecisionKind.Deny, PromptBoundaryDenyMessage),
            ChatIntentFamily.DestructiveCommand => new ChatPolicyDecision(ChatPolicyDecisionKind.Deny, DestructiveCommandDenyMessage),
            ChatIntentFamily.DestructiveAction => new ChatPolicyDecision(ChatPolicyDecisionKind.Deny, WriteActionDenyMessage),
            ChatIntentFamily.DocumentLookup or ChatIntentFamily.OwnDetail => new ChatPolicyDecision(ChatPolicyDecisionKind.ExecuteRag),
            ChatIntentFamily.OwnSummary or ChatIntentFamily.ApprovalQueue => new ChatPolicyDecision(ChatPolicyDecisionKind.ExecuteReporting),
            ChatIntentFamily.Aggregate => DecideAggregate(profile, query),
            ChatIntentFamily.Comparison or ChatIntentFamily.Ranking => DecideSensitiveAggregate(profile),
            _ => classification.Mode == ChatExecutionMode.Rag
                ? new ChatPolicyDecision(ChatPolicyDecisionKind.ExecuteRag)
                : new ChatPolicyDecision(ChatPolicyDecisionKind.ExecuteReporting)
        };
    }

    private static bool IsAmbiguousQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 10)
            return true;

        var normalized = IntentTextNormalizer.Normalize(query);

        // Patterns that are too incomplete/vague without additional context
        var vaguePatterns = new[]
        {
            "cho toi xem",
            "xem gi",
            "xem nao",
            "xem di",
            "nhung ai",
            "ai da",
            "ai duyet",
            "ai phe duyet",
            "gi the",
            "gi vay",
            "the nao"
        };

        foreach (var pattern in vaguePatterns)
        {
            if (normalized.Contains(pattern))
                return true;
        }

        return false;
    }

    private static ChatPolicyDecision DecideAggregate(ChatAuthorizationProfile profile, string query)
    {
        if (profile.Role == RoleType.Staff && MentionsBroaderScope(query))
            return new ChatPolicyDecision(ChatPolicyDecisionKind.Deny, DenyMessage);

        return new ChatPolicyDecision(ChatPolicyDecisionKind.ExecuteReporting);
    }

    private static ChatPolicyDecision DecideSensitiveAggregate(ChatAuthorizationProfile profile)
    {
        if (profile.Role == RoleType.Staff)
            return new ChatPolicyDecision(ChatPolicyDecisionKind.Deny, DenyMessage);

        return new ChatPolicyDecision(ChatPolicyDecisionKind.ExecuteReporting);
    }

    private static bool MentionsBroaderScope(string query)
    {
        var normalized = IntentTextNormalizer.Normalize(query);
        return ScopeKeywords.MentionsDepartmentScope(normalized) ||
               ScopeKeywords.MentionsTenantScope(normalized) ||
               ScopeKeywords.MentionsCrossScope(normalized) ||
               ScopeKeywords.MentionsGenericGroupScope(normalized);
    }
}
