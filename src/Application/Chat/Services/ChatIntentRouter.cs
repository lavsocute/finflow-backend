using System.Text.RegularExpressions;
using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Application.Chat.Services;

public sealed partial class ChatIntentRouter : IChatIntentRouter
{
    [GeneratedRegex(@"^\s*(hello|hi|hey|xin chao|chao|alo)\s*([!.?,]+)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GreetingPattern();

    [GeneratedRegex(@"\b(ban la ai|ban giup duoc gi|ban co the giup gi|cam on|thanks|thank you|giup toi voi|help me|help)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SmallTalkPattern();

    [GeneratedRegex(@"\b(viet lai|rewrite|paraphrase|rephrase|tom tat|tom luoc|summarize|summary|goi y|suggest|dat ten|naming|ten hang muc|category name)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ProductivityPattern();

    [GeneratedRegex(@"\b(code|ma nguon|doan ma|lap trinh|program|programming|py\s*thon|python|java|c#|java\s*script|javascript|type\s*script|typescript|s\s*q\s*l|sql|query|cau lenh|regex|script|snippet|etl|parse|api|function|class|method|dataframe|pandas|groupby)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ProgrammingPattern();

    [GeneratedRegex(@"\b(co nen|nen duyet|nen .* khong|nen .* chu|xung dang|duyet giup|chon giup|quyet dinh giup|dau hieu gian lan|gian lan|fraud|compliance|rui ro|risk|co mui|co phot|hop tac|approve giup|safe ko|an toan khong)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveAdvicePattern();

    [GeneratedRegex(@"\b(preapproved|approval|approve|rejected|readyforapproval|ready for approval|cho duyet|dang cho duyet|phe duyet|duyet)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ApprovalReportingPattern();

    [GeneratedRegex(@"\b(xu huong|trend|bien dong|gan day|recent months|last \d+ months)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TrendReportingPattern();

    [GeneratedRegex(@"\b(nha cung cap nao|vendor nao|top vendor|merchant nao)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VendorReportingPattern();

    [GeneratedRegex(@"\b(ngan sach|budget|han muc|utilization|muc su dung|budget remaining)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BudgetReportingPattern();

    [GeneratedRegex(@"\b(so sanh|compare|comparison|versus|vs\.?|nguoi khac|dong nghiep|others|other people|top spender|xep hang|dung thu|leaderboard|rank|ranking|top|nhieu hon|it hon|who spent more|ai chi|ai spend|nhan vien nao|employee nao|burn|burning|burned|dot tien|chay ngan sach|chay quy|ngoai toi)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ComparisonReportingPattern();

    [GeneratedRegex(@"\b(tong|bao nhieu|how much|total|spent|chi bao nhieu|budget|con bao nhieu|remaining)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReportingPattern();

    [GeneratedRegex(@"\b(receipt|invoice|hoa don|chung tu|expense\s+id|vendor)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RagPattern();

    public ChatIntentClassification Classify(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new ChatIntentClassification(ChatExecutionMode.Rag, "empty-default", ChatIntentFamily.Unknown, ChatScopeConfidence.Ambiguous);
        }

        var normalizedQuery = IntentTextNormalizer.Normalize(query);

        if (GreetingPattern().IsMatch(normalizedQuery))
        {
            return new ChatIntentClassification(ChatExecutionMode.Greeting, "keyword-greeting", ChatIntentFamily.Greeting, ChatScopeConfidence.Explicit);
        }

        if (SmallTalkPattern().IsMatch(normalizedQuery))
        {
            return new ChatIntentClassification(ChatExecutionMode.General, "small-talk-general", ChatIntentFamily.SmallTalk, ChatScopeConfidence.Explicit);
        }

        if (ProductivityPattern().IsMatch(normalizedQuery))
        {
            return new ChatIntentClassification(ChatExecutionMode.General, "productivity-general", ChatIntentFamily.Productivity, ChatScopeConfidence.Explicit);
        }

        if (ProgrammingPattern().IsMatch(normalizedQuery))
        {
            return new ChatIntentClassification(ChatExecutionMode.General, "programming-deny", ChatIntentFamily.Programming, ChatScopeConfidence.Forbidden);
        }

        if (SensitiveAdvicePattern().IsMatch(normalizedQuery))
        {
            return new ChatIntentClassification(ChatExecutionMode.General, "sensitive-advice-deny", ChatIntentFamily.SensitiveAdvice, ChatScopeConfidence.Forbidden);
        }

        if (ApprovalReportingPattern().IsMatch(normalizedQuery))
        {
            var confidence = MentionsDepartmentScope(normalizedQuery) || MentionsOwnScope(normalizedQuery)
                ? ChatScopeConfidence.Explicit
                : ChatScopeConfidence.Ambiguous;
            return new ChatIntentClassification(ChatExecutionMode.Reporting, "approval-reporting", ChatIntentFamily.ApprovalQueue, confidence);
        }

        if (TrendReportingPattern().IsMatch(normalizedQuery))
        {
            return new ChatIntentClassification(
                ChatExecutionMode.Reporting,
                "trend-reporting",
                ChatIntentFamily.Aggregate,
                ResolveScopeConfidence(normalizedQuery));
        }

        if (VendorReportingPattern().IsMatch(normalizedQuery))
        {
            return new ChatIntentClassification(
                ChatExecutionMode.Reporting,
                "vendor-reporting",
                ChatIntentFamily.Aggregate,
                ResolveScopeConfidence(normalizedQuery));
        }

        if (ComparisonReportingPattern().IsMatch(normalizedQuery))
        {
            return new ChatIntentClassification(
                ChatExecutionMode.Reporting,
                "comparison-reporting",
                IsRankingPhrase(normalizedQuery) ? ChatIntentFamily.Ranking : ChatIntentFamily.Comparison,
                ResolveSensitiveScopeConfidence(normalizedQuery));
        }

        if (BudgetReportingPattern().IsMatch(normalizedQuery))
        {
            return new ChatIntentClassification(
                ChatExecutionMode.Reporting,
                "budget-reporting",
                ChatIntentFamily.Aggregate,
                ResolveScopeConfidence(normalizedQuery));
        }

        if (ReportingPattern().IsMatch(normalizedQuery))
        {
            return new ChatIntentClassification(
                ChatExecutionMode.Reporting,
                "keyword-reporting",
                IsOwnSummaryPhrase(normalizedQuery) ? ChatIntentFamily.OwnSummary : ChatIntentFamily.Aggregate,
                ResolveScopeConfidence(normalizedQuery));
        }

        if (RagPattern().IsMatch(normalizedQuery))
        {
            return new ChatIntentClassification(ChatExecutionMode.Rag, "keyword-rag", ChatIntentFamily.DocumentLookup, ChatScopeConfidence.Explicit);
        }

        if (IsLowSignalQuery(normalizedQuery))
        {
            return new ChatIntentClassification(ChatExecutionMode.General, "low-signal-general", ChatIntentFamily.LowSignal, ChatScopeConfidence.Ambiguous);
        }

        return new ChatIntentClassification(ChatExecutionMode.Rag, "default-rag", ChatIntentFamily.Unknown, ChatScopeConfidence.Ambiguous);
    }

    private static ChatScopeConfidence ResolveScopeConfidence(string normalizedQuery)
    {
        var mentionsOwnScope = MentionsOwnScope(normalizedQuery);
        var mentionsOwnedDepartmentScope = MentionsOwnedDepartmentScope(normalizedQuery);
        var mentionsDepartmentScope = MentionsDepartmentScope(normalizedQuery);
        var mentionsTenantScope = MentionsTenantScope(normalizedQuery);
        var mentionsCrossScope = MentionsCrossScope(normalizedQuery);

        if (mentionsCrossScope && (mentionsOwnedDepartmentScope || mentionsDepartmentScope || mentionsTenantScope))
            return ChatScopeConfidence.Ambiguous;

        if (mentionsTenantScope)
            return ChatScopeConfidence.Explicit;

        if (mentionsDepartmentScope && !mentionsOwnedDepartmentScope)
            return ChatScopeConfidence.Explicit;

        if (mentionsOwnedDepartmentScope)
            return ChatScopeConfidence.SafeInferred;

        if (mentionsOwnScope && !mentionsCrossScope)
            return ChatScopeConfidence.Explicit;

        return ChatScopeConfidence.Ambiguous;
    }

    private static ChatScopeConfidence ResolveSensitiveScopeConfidence(string normalizedQuery)
    {
        var mentionsOwnedDepartmentScope = MentionsOwnedDepartmentScope(normalizedQuery);
        var mentionsDepartmentScope = MentionsDepartmentScope(normalizedQuery);
        var mentionsTenantScope = MentionsTenantScope(normalizedQuery);
        var mentionsCrossScope = MentionsCrossScope(normalizedQuery);
        var mentionsGenericGroupScope = MentionsGenericGroupScope(normalizedQuery);

        if (mentionsTenantScope && mentionsCrossScope)
            return ChatScopeConfidence.Explicit;

        if (mentionsGenericGroupScope)
            return ChatScopeConfidence.Ambiguous;

        if (mentionsCrossScope && (mentionsOwnedDepartmentScope || mentionsDepartmentScope))
            return ChatScopeConfidence.Ambiguous;

        if (mentionsOwnedDepartmentScope)
            return ChatScopeConfidence.SafeInferred;

        if (mentionsDepartmentScope || mentionsTenantScope)
            return ChatScopeConfidence.Explicit;

        return ChatScopeConfidence.Ambiguous;
    }

    private static bool IsOwnSummaryPhrase(string normalizedQuery)
    {
        if (MentionsDepartmentScope(normalizedQuery) || MentionsTenantScope(normalizedQuery))
            return false;

        return MentionsOwnScope(normalizedQuery) &&
               !ComparisonReportingPattern().IsMatch(normalizedQuery);
    }

    private static bool IsRankingPhrase(string normalizedQuery)
    {
        return normalizedQuery.Contains("xep hang", StringComparison.Ordinal) ||
               normalizedQuery.Contains("dung thu", StringComparison.Ordinal) ||
               normalizedQuery.Contains("top", StringComparison.Ordinal) ||
               normalizedQuery.Contains("rank", StringComparison.Ordinal) ||
               ((normalizedQuery.Contains("ai ", StringComparison.Ordinal) ||
                 normalizedQuery.Contains("nhan vien nao", StringComparison.Ordinal) ||
                 normalizedQuery.Contains("employee nao", StringComparison.Ordinal) ||
                 normalizedQuery.Contains("dua nao", StringComparison.Ordinal) ||
                 normalizedQuery.Contains("who ", StringComparison.Ordinal)) &&
                normalizedQuery.Contains("nhat", StringComparison.Ordinal));
    }

    private static bool MentionsOwnScope(string normalizedQuery) =>
        normalizedQuery.Contains(" cua toi", StringComparison.Ordinal) ||
        normalizedQuery.Contains(" cua em", StringComparison.Ordinal) ||
        normalizedQuery.Contains(" cua minh", StringComparison.Ordinal) ||
        normalizedQuery.Contains(" toi ", StringComparison.Ordinal) ||
        normalizedQuery.StartsWith("toi ", StringComparison.Ordinal) ||
        normalizedQuery.Contains(" my ", StringComparison.Ordinal) ||
        normalizedQuery.StartsWith("my ", StringComparison.Ordinal);

    private static bool MentionsOwnedDepartmentScope(string normalizedQuery) =>
        normalizedQuery.Contains("team toi", StringComparison.Ordinal) ||
        normalizedQuery.Contains("phong ban toi", StringComparison.Ordinal) ||
        normalizedQuery.Contains("bo phan toi", StringComparison.Ordinal) ||
        normalizedQuery.Contains("my team", StringComparison.Ordinal);

    private static bool MentionsDepartmentScope(string normalizedQuery) =>
        normalizedQuery.Contains("phong ban", StringComparison.Ordinal) ||
        normalizedQuery.Contains("department", StringComparison.Ordinal) ||
        normalizedQuery.Contains("team", StringComparison.Ordinal);

    private static bool MentionsTenantScope(string normalizedQuery) =>
        normalizedQuery.Contains("toan cong ty", StringComparison.Ordinal) ||
        normalizedQuery.Contains("cong ty", StringComparison.Ordinal) ||
        normalizedQuery.Contains("tenant", StringComparison.Ordinal) ||
        normalizedQuery.Contains("workspace", StringComparison.Ordinal) ||
        normalizedQuery.Contains("all company", StringComparison.Ordinal);

    private static bool MentionsCrossScope(string normalizedQuery) =>
        normalizedQuery.Contains("nguoi khac", StringComparison.Ordinal) ||
        normalizedQuery.Contains("dong nghiep", StringComparison.Ordinal) ||
        normalizedQuery.Contains("others", StringComparison.Ordinal) ||
        normalizedQuery.Contains("other people", StringComparison.Ordinal) ||
        normalizedQuery.Contains("ngoai toi", StringComparison.Ordinal) ||
        normalizedQuery.Contains("team kia", StringComparison.Ordinal) ||
        normalizedQuery.Contains("phong khac", StringComparison.Ordinal) ||
        normalizedQuery.Contains("bo phan khac", StringComparison.Ordinal) ||
        normalizedQuery.Contains("khac", StringComparison.Ordinal) ||
        normalizedQuery.Contains("so voi", StringComparison.Ordinal) ||
        normalizedQuery.Contains("versus", StringComparison.Ordinal) ||
        normalizedQuery.Contains(" vs", StringComparison.Ordinal);

    private static bool MentionsGenericGroupScope(string normalizedQuery) =>
        normalizedQuery.Contains("team nao", StringComparison.Ordinal) ||
        normalizedQuery.Contains("phong nao", StringComparison.Ordinal) ||
        normalizedQuery.Contains("bo phan nao", StringComparison.Ordinal) ||
        normalizedQuery.Contains("department nao", StringComparison.Ordinal);

    private static bool IsLowSignalQuery(string normalizedQuery)
    {
        var tokens = normalizedQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 1 && tokens[0].Length <= 3 && tokens[0].All(char.IsLetterOrDigit))
            return true;

        return false;
    }
}
