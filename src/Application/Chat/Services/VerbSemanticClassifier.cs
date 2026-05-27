namespace FinFlow.Application.Chat.Services;

public enum VerbKind { Query, Action, Neutral }

/// <summary>
/// Classifies Vietnamese and English verbs as Query, Action, or Neutral intent.
/// </summary>
public sealed class VerbSemanticClassifier
{
    private static readonly string[] ActionPhrases =
    [
        "xoa",
        "delete",
        "drop",
        "wipe",
        "loai bo",
        "remove",
        "tieu huy",
        "pha huy",
        "destroy",
        "clear"
    ];

    private static readonly string[] QueryPhrases =
    [
        "bao nhieu",
        "how much",
        "what is",
        "cho toi xem",
        "xem",
        "show",
        "list",
        "tim",
        "kiem tra"
    ];

    private static readonly Dictionary<string, VerbKind> VerbDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        // ACTION verbs - destructive/modifying
        { "xoa", VerbKind.Action },
        { "xóa", VerbKind.Action },
        { "delete", VerbKind.Action },
        { "drop", VerbKind.Action },
        { "wipe", VerbKind.Action },
        { "clear", VerbKind.Action },
        { "pha", VerbKind.Action },
        { "phá", VerbKind.Action },
        { "tao", VerbKind.Action },
        { "tạo", VerbKind.Action },
        { "create", VerbKind.Action },
        { "sua", VerbKind.Action },
        { "sửa", VerbKind.Action },
        { "update", VerbKind.Action },
        { "modify", VerbKind.Action },
        { "loai bo", VerbKind.Action },
        { "loại bỏ", VerbKind.Action },
        { "remove", VerbKind.Action },
        { "huy", VerbKind.Action },
        { "hủy", VerbKind.Action },
        { "cancel", VerbKind.Action },
        { "tieu huy", VerbKind.Action },
        { "tiêu hủy", VerbKind.Action },
        { "destroy", VerbKind.Action },
        { "insert", VerbKind.Action },
        { "add", VerbKind.Action },
        { "approve", VerbKind.Action },
        { "reject", VerbKind.Action },
        { "duyet", VerbKind.Action },
        { "duyệt", VerbKind.Action },
        { "confirm", VerbKind.Action },
        { "submit", VerbKind.Action },
        { "edit", VerbKind.Action },
        { "patch", VerbKind.Action },
        { "replace", VerbKind.Action },
        { "rename", VerbKind.Action },
        { "move", VerbKind.Action },
        { "copy", VerbKind.Action },
        { "archive", VerbKind.Action },

        // QUERY verbs - reading only
        { "xem", VerbKind.Query },
        { "show", VerbKind.Query },
        { "get", VerbKind.Query },
        { "find", VerbKind.Query },
        { "check", VerbKind.Query },
        { "view", VerbKind.Query },
        { "look", VerbKind.Query },
        { "see", VerbKind.Query },
        { "tell", VerbKind.Query },
        { "ask", VerbKind.Query },
        { "what", VerbKind.Query },
        { "which", VerbKind.Query },
        { "how", VerbKind.Query },
        { "who", VerbKind.Query },
        { "where", VerbKind.Query },
        { "when", VerbKind.Query },
        { "why", VerbKind.Query },
        { "tim", VerbKind.Query },
        { "tìm", VerbKind.Query },
        { "kiem tra", VerbKind.Query },
        { "kiểm tra", VerbKind.Query },
        { "hoi", VerbKind.Query },
        { "hỏi", VerbKind.Query },
        { "tra", VerbKind.Query },
        { "truy van", VerbKind.Query },
        { "truy vấn", VerbKind.Query },
        { "query", VerbKind.Query },
        { "list", VerbKind.Query },
        { "search", VerbKind.Query },
        { "fetch", VerbKind.Query },
        { "retrieve", VerbKind.Query },
        { "count", VerbKind.Query },
        { "sum", VerbKind.Query },
        { "average", VerbKind.Query },
        { "total", VerbKind.Query },
        { "calculate", VerbKind.Query },
        { "report", VerbKind.Query },
        { "display", VerbKind.Query },
        { "explain", VerbKind.Query },
        { "describe", VerbKind.Query },
        { "identify", VerbKind.Query },
    };

    public VerbKind ClassifyQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return VerbKind.Neutral;

        var normalized = IntentTextNormalizer.Normalize(query);

        if (ActionPhrases.Any(phrase => ContainsPhrase(normalized, phrase)))
            return VerbKind.Action;

        if (QueryPhrases.Any(phrase => ContainsPhrase(normalized, phrase)))
            return VerbKind.Query;

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var actionCount = 0;
        var queryCount = 0;

        foreach (var token in tokens)
        {
            if (VerbDictionary.TryGetValue(token, out var kind))
            {
                switch (kind)
                {
                    case VerbKind.Action:
                        actionCount++;
                        break;
                    case VerbKind.Query:
                        queryCount++;
                        break;
                }
            }
        }

        if (actionCount > queryCount)
            return VerbKind.Action;

        if (queryCount > actionCount)
            return VerbKind.Query;

        return VerbKind.Neutral;
    }

    private static bool ContainsPhrase(string normalizedQuery, string phrase)
    {
        var paddedQuery = $" {normalizedQuery} ";
        var paddedPhrase = $" {phrase} ";
        return paddedQuery.Contains(paddedPhrase, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsQueryVerb(string token) =>
        VerbDictionary.TryGetValue(token, out var kind) && kind == VerbKind.Query;

    public static bool IsActionVerb(string token) =>
        VerbDictionary.TryGetValue(token, out var kind) && kind == VerbKind.Action;
}
