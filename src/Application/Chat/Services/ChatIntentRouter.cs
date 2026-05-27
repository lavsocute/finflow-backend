using System.Text.RegularExpressions;
using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Routes incoming chat queries to the appropriate execution mode using regex patterns and semantic classification.
/// </summary>
public sealed partial class ChatIntentRouter : IChatIntentRouter
{
    private readonly ITextNormalizer _textNormalizer;
    private static readonly VerbSemanticClassifier _verbClassifier = new();

    public ChatIntentRouter(ITextNormalizer? textNormalizer = null)
    {
        _textNormalizer = textNormalizer ?? new TextNormalizer();
    }

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

        var normalizedQuery = _textNormalizer.Normalize(query);

        if (GreetingPattern().IsMatch(normalizedQuery))
        {
            return new ChatIntentClassification(ChatExecutionMode.Greeting, "keyword-greeting", ChatIntentFamily.Greeting, ChatScopeConfidence.Explicit);
        }

        if (IsScopeOnlyQuery(normalizedQuery))
        {
            return new ChatIntentClassification(ChatExecutionMode.General, "scope-only-low-signal", ChatIntentFamily.LowSignal, ChatScopeConfidence.Ambiguous);
        }

        if (IsDestructiveOperationRequest(normalizedQuery))
        {
            return new ChatIntentClassification(ChatExecutionMode.General, "destructive-action-deny", ChatIntentFamily.DestructiveAction, ChatScopeConfidence.Forbidden);
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
            return new ChatIntentClassification(ChatExecutionMode.General, "programming-deny", ChatIntentFamily.Programming, ChatScopeConfidence.Explicit);
        }

        if (SensitiveAdvicePattern().IsMatch(normalizedQuery))
        {
            return new ChatIntentClassification(ChatExecutionMode.General, "sensitive-advice-deny", ChatIntentFamily.SensitiveAdvice, ChatScopeConfidence.Explicit);
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

        // Semantic classification as fallback - only block if no other pattern matched
        // This catches action verbs that didn't match any specific pattern
        var semanticKind = _verbClassifier.ClassifyQuery(normalizedQuery);
        var normalizedTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Check if any query verb is present - if so, don't block as action
        var hasQueryVerb = normalizedTokens.Any(t => VerbSemanticClassifier.IsQueryVerb(t));

        // Only block if semantic is action and no query verbs present
        if (semanticKind == VerbKind.Action && !hasQueryVerb)
        {
            return new ChatIntentClassification(ChatExecutionMode.General, "semantic-action-deny", ChatIntentFamily.DestructiveAction, ChatScopeConfidence.Forbidden);
        }

        return new ChatIntentClassification(ChatExecutionMode.Rag, "default-rag", ChatIntentFamily.Unknown, ChatScopeConfidence.Ambiguous);
    }

    private static bool IsDestructiveOperationRequest(string normalizedQuery)
    {
        if (IsDestructiveDefinitionQuery(normalizedQuery) || IsDeleteButtonLocationQuery(normalizedQuery))
            return false;

        var destructiveTerms = new[]
        {
            "xoa",
            "delete",
            "drop",
            "wipe",
            "loai bo",
            "remove",
            "tieu huy",
            "pha huy",
            "destroy"
        };

        return destructiveTerms.Any(term => ContainsWholePhrase(normalizedQuery, term));
    }

    private static bool IsScopeOnlyQuery(string normalizedQuery) =>
        normalizedQuery is
            "toan cong ty" or
            "cong ty" or
            "company" or
            "all company" or
            "phong ban" or
            "department" or
            "team" or
            "phong ban cua toi" or
            "phong ban toi" or
            "bo phan cua toi" or
            "bo phan toi" or
            "team toi" or
            "my team" or
            "cua toi" or
            "cua em" or
            "cua minh" or
            "toi" or
            "minh" or
            "my";

    private static bool IsDestructiveDefinitionQuery(string normalizedQuery) =>
        normalizedQuery.Contains("xoa la gi", StringComparison.Ordinal) ||
        normalizedQuery.Contains("delete la gi", StringComparison.Ordinal) ||
        normalizedQuery.Contains("delete mean", StringComparison.Ordinal);

    private static bool IsDeleteButtonLocationQuery(string normalizedQuery) =>
        normalizedQuery.Contains("nut xoa", StringComparison.Ordinal) &&
        (normalizedQuery.Contains("o dau", StringComparison.Ordinal) ||
         normalizedQuery.Contains("where", StringComparison.Ordinal));

    private static bool ContainsWholePhrase(string normalizedQuery, string phrase)
    {
        var paddedQuery = $" {normalizedQuery} ";
        var paddedPhrase = $" {phrase} ";
        return paddedQuery.Contains(paddedPhrase, StringComparison.Ordinal);
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
        ScopeKeywords.MentionsOwnedDepartmentScope(normalizedQuery);

    private static bool MentionsDepartmentScope(string normalizedQuery) =>
        ScopeKeywords.MentionsDepartmentScope(normalizedQuery);

    private static bool MentionsTenantScope(string normalizedQuery) =>
        ScopeKeywords.MentionsTenantScope(normalizedQuery);

    private static bool MentionsCrossScope(string normalizedQuery) =>
        ScopeKeywords.MentionsCrossScope(normalizedQuery);

    private static bool MentionsGenericGroupScope(string normalizedQuery) =>
        ScopeKeywords.MentionsGenericGroupScope(normalizedQuery);

    private static bool IsLowSignalQuery(string normalizedQuery)
    {
        var tokens = normalizedQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 1 && tokens[0].Length <= 3 && tokens[0].All(char.IsLetterOrDigit))
            return true;

        return false;
    }
}
