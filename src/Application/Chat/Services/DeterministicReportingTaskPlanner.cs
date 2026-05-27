using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Application.Chat.Services;

internal sealed class DeterministicReportingTaskPlanner
{
    private readonly ITextNormalizer _textNormalizer;

    private static readonly SemanticRule[] Rules =
    [
        new(
            ChatReportingTask.EntityStatusLookup,
            ChatExecutionMode.Rag,
            ChatIntentFamily.DocumentLookup,
            "deterministic-entity-status-lookup",
            RequiredAny:
            [
                "trang thai", "tinh trang", "status", "da duyet", "duoc duyet", "approved",
                "cho duyet", "ready for approval", "rejected", "bi tu choi"
            ],
            ContextualAny:
            [
                "do", "khoan do", "chung tu do", "hoa don do", "expense do", "that", "it"
            ],
            NegativeAny:
            [
                "duyet giup", "approve giup", "phe duyet giup", "duyet luon", "approve now"
            ]),
        new(
            ChatReportingTask.ApprovalQueue,
            ChatExecutionMode.Reporting,
            ChatIntentFamily.ApprovalQueue,
            "deterministic-approval-queue",
            RequiredAny:
            [
                "cho duyet", "dang cho duyet", "ready for approval", "approval queue",
                "pending approval", "can duyet", "waiting approval"
            ]),
        new(
            ChatReportingTask.Comparison,
            ChatExecutionMode.Reporting,
            ChatIntentFamily.Comparison,
            "deterministic-comparison",
            RequiredAny:
            [
                "so voi", "compare", "comparison", "versus", "vs", "tang hay giam",
                "tang giam", "increase", "decrease", "higher", "lower", "cau dau tien",
                "cau truoc", "previous one"
            ]),
        new(
            ChatReportingTask.VendorRanking,
            ChatExecutionMode.Reporting,
            ChatIntentFamily.Ranking,
            "deterministic-vendor-ranking",
            RequiredAllAnyGroups:
            [
                ["vendor", "nha cung cap", "merchant"],
                ["top", "nhieu nhat", "chi nhieu", "ranking", "xep hang", "dong gop"]
            ]),
        new(
            ChatReportingTask.Summary,
            ChatExecutionMode.Reporting,
            ChatIntentFamily.Aggregate,
            "deterministic-summary",
            RequiredAny:
            [
                "tong chi", "total spend", "total spending", "spent", "spending picture",
                "buc tranh tai chinh", "financial picture", "bao cao tong quan",
                "tong quan", "burn rate", "chi bao nhieu", "bao nhieu"
            ],
            RequiredAllAnyGroups:
            [
                ["chi", "spend", "spending", "expense", "tai chinh", "financial", "burn"],
                ["tong", "total", "picture", "buc tranh", "tong quan", "bao nhieu", "rate"]
            ])
    ];

    public DeterministicReportingTaskPlanner(ITextNormalizer? textNormalizer = null)
    {
        _textNormalizer = textNormalizer ?? new TextNormalizer();
    }

    public ChatIntentClassification? TryClassify(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var normalized = NormalizePhraseBoundaries(_textNormalizer.Normalize(query));
        foreach (var rule in Rules)
        {
            if (!rule.IsMatch(normalized))
                continue;

            return new ChatIntentClassification(
                rule.Mode,
                rule.Reason,
                rule.Family,
                ResolveScopeConfidence(normalized),
                rule.ReportingTask);
        }

        return null;
    }

    private static ChatScopeConfidence ResolveScopeConfidence(string normalizedQuery)
    {
        if (ScopeKeywords.MentionsTenantScope(normalizedQuery) ||
            ScopeKeywords.MentionsDepartmentScope(normalizedQuery) ||
            ScopeKeywords.MentionsOwnScope(normalizedQuery) ||
            ScopeKeywords.MentionsOwnedDepartmentScope(normalizedQuery))
        {
            return ChatScopeConfidence.Explicit;
        }

        if (ContainsAny(normalizedQuery,
            [
                "do", "khoang do", "cau dau tien", "cau truoc", "thang truoc",
                "same", "that", "previous", "prev"
            ]))
        {
            return ChatScopeConfidence.SafeInferred;
        }

        return ChatScopeConfidence.Ambiguous;
    }

    private static bool ContainsAny(string normalizedQuery, IReadOnlyList<string> phrases) =>
        phrases.Any(phrase => ContainsPhrase(normalizedQuery, phrase));

    private static string NormalizePhraseBoundaries(string normalizedQuery)
    {
        var buffer = normalizedQuery
            .Select(static character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray();
        return string.Join(
            ' ',
            new string(buffer).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static bool ContainsPhrase(string normalizedQuery, string phrase)
    {
        var paddedQuery = $" {normalizedQuery} ";
        var paddedPhrase = $" {phrase} ";
        return paddedQuery.Contains(paddedPhrase, StringComparison.Ordinal);
    }

    private sealed record SemanticRule(
        ChatReportingTask ReportingTask,
        ChatExecutionMode Mode,
        ChatIntentFamily Family,
        string Reason,
        IReadOnlyList<string>? RequiredAny = null,
        IReadOnlyList<IReadOnlyList<string>>? RequiredAllAnyGroups = null,
        IReadOnlyList<string>? ContextualAny = null,
        IReadOnlyList<string>? NegativeAny = null)
    {
        public bool IsMatch(string normalizedQuery)
        {
            if (NegativeAny is not null && ContainsAny(normalizedQuery, NegativeAny))
                return false;

            var hasRequiredAny = RequiredAny is null || ContainsAny(normalizedQuery, RequiredAny);
            var hasAllGroups = RequiredAllAnyGroups is null ||
                RequiredAllAnyGroups.All(group => ContainsAny(normalizedQuery, group));
            var hasContext = ContextualAny is null || ContainsAny(normalizedQuery, ContextualAny);

            return hasRequiredAny && hasAllGroups && hasContext;
        }
    }
}
