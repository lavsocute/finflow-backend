using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Application.Chat.Services;

public enum SemanticIntentKind
{
    Query,
    Action
}

public enum SemanticIntentConfidence
{
    High,
    Medium,
    Low
}

/// <summary>
/// Classifies query verbs as either Query or Action intent and provides confidence levels.
/// </summary>
public sealed class SemanticIntentClassifier
{
    private readonly ITextNormalizer _textNormalizer;

    public SemanticIntentClassifier(ITextNormalizer textNormalizer)
    {
        _textNormalizer = textNormalizer;
    }

    private static readonly Dictionary<string, SemanticIntentKind> VerbDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        // Action verbs - operations that modify state
        { "tao", SemanticIntentKind.Action },
        { "tao moi", SemanticIntentKind.Action },
        { "xoa", SemanticIntentKind.Action },
        { "xoa bo", SemanticIntentKind.Action },
        { "sua", SemanticIntentKind.Action },
        { "cap nhat", SemanticIntentKind.Action },
        { "capnhat", SemanticIntentKind.Action },
        { "them", SemanticIntentKind.Action },
        { "them vao", SemanticIntentKind.Action },
        { "lenh", SemanticIntentKind.Action },
        { "duyet", SemanticIntentKind.Action },
        { "phe duyet", SemanticIntentKind.Action },
        { "approve", SemanticIntentKind.Action },
        { "reject", SemanticIntentKind.Action },
        { "tu choi", SemanticIntentKind.Action },
        { "huy", SemanticIntentKind.Action },
        { "cancel", SemanticIntentKind.Action },
        { "submit", SemanticIntentKind.Action },
        { "gui", SemanticIntentKind.Action },
        { "send", SemanticIntentKind.Action },
        { "publish", SemanticIntentKind.Action },
        { "ban hanh", SemanticIntentKind.Action },
        { "export", SemanticIntentKind.Action },
        { "import", SemanticIntentKind.Action },
        { "create", SemanticIntentKind.Action },
        { "delete", SemanticIntentKind.Action },
        { "remove", SemanticIntentKind.Action },
        { "update", SemanticIntentKind.Action },
        { "modify", SemanticIntentKind.Action },
        { "add", SemanticIntentKind.Action },
        { "execute", SemanticIntentKind.Action },
        { "run", SemanticIntentKind.Action },
        { "chay", SemanticIntentKind.Action },
        { "ket xuat", SemanticIntentKind.Action },
        { "ketxuat", SemanticIntentKind.Action },

        // Query verbs - information retrieval
        { "xem", SemanticIntentKind.Query },
        { "hien thi", SemanticIntentKind.Query },
        { "hienthi", SemanticIntentKind.Query },
        { "tim", SemanticIntentKind.Query },
        { "tim kiem", SemanticIntentKind.Query },
        { "timkiem", SemanticIntentKind.Query },
        { "lay", SemanticIntentKind.Query },
        { "lay ra", SemanticIntentKind.Query },
        { "liet ke", SemanticIntentKind.Query },
        { "lietke", SemanticIntentKind.Query },
        { "danh sach", SemanticIntentKind.Query },
        { "danhsach", SemanticIntentKind.Query },
        { "list", SemanticIntentKind.Query },
        { "show", SemanticIntentKind.Query },
        { "get", SemanticIntentKind.Query },
        { "lay ve", SemanticIntentKind.Query },
        { "layve", SemanticIntentKind.Query },
        { "bao cao", SemanticIntentKind.Query },
        { "baocao", SemanticIntentKind.Query },
        { "report", SemanticIntentKind.Query },
        { "tong hop", SemanticIntentKind.Query },
        { "tonghop", SemanticIntentKind.Query },
        { "thong ke", SemanticIntentKind.Query },
        { "thongke", SemanticIntentKind.Query },
        { "统计", SemanticIntentKind.Query },
        { "so sanh", SemanticIntentKind.Query },
        { "sosanh", SemanticIntentKind.Query },
        { "compare", SemanticIntentKind.Query },
        { "phan tich", SemanticIntentKind.Query },
        { "phantich", SemanticIntentKind.Action },
        { "tra cuu", SemanticIntentKind.Query },
        { "tracuu", SemanticIntentKind.Query },
        { "check", SemanticIntentKind.Query },
        { "xac minh", SemanticIntentKind.Query },
        { "xacminh", SemanticIntentKind.Query },
        { "verify", SemanticIntentKind.Query },
        { "ask", SemanticIntentKind.Query },
        { "question", SemanticIntentKind.Query },
        { "hoi", SemanticIntentKind.Query },
        { "muon biet", SemanticIntentKind.Query },
        { "muonbiet", SemanticIntentKind.Query },
        { "want to know", SemanticIntentKind.Query },
        { "can I", SemanticIntentKind.Query },
        { "what is", SemanticIntentKind.Query },
        { "where is", SemanticIntentKind.Query },
        { "who is", SemanticIntentKind.Query },
        { "how much", SemanticIntentKind.Query },
        { "bao nhieu", SemanticIntentKind.Query },
        { "tong", SemanticIntentKind.Query },
        { "total", SemanticIntentKind.Query },
        { "chi phi", SemanticIntentKind.Query },
        { "chiphi", SemanticIntentKind.Query },
        { "cost", SemanticIntentKind.Query },
        { "expense", SemanticIntentKind.Query },
        { "status", SemanticIntentKind.Query },
        { "trang thai", SemanticIntentKind.Query },
        { "trangthai", SemanticIntentKind.Query },
    };

    private static readonly HashSet<string> ActionVerbPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "tao", "xoa", "sua", "cap nhat", "them", "duyet", "huy", "gui", "submit", "export", "import",
        "create", "delete", "update", "remove", "modify", "add", "execute", "run", "chay",
        "ban hanh", "phe duyet", "approve", "reject", "ket xuat"
    };

    private static readonly HashSet<string> QueryVerbPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "xem", "hien thi", "tim", "lay", "liet ke", "danh sach", "list", "show", "get",
        "bao cao", "tong hop", "thong ke", "so sanh", "tra cuu", "check", "xac minh",
        "verify", "ask", "question", "hoi", "muon biet", "want", "can I", "what", "where", "who", "how",
        "bao nhieu", "tong", "total", "chi phi", "cost", "expense", "status", "trang thai"
    };

    public (SemanticIntentKind Kind, SemanticIntentConfidence Confidence) ClassifyVerbIntent(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return (SemanticIntentKind.Query, SemanticIntentConfidence.Low);

        var normalizedQuery = _textNormalizer.Normalize(query);
        var verbs = ExtractVerbs(normalizedQuery);

        if (verbs.Count == 0)
            return (SemanticIntentKind.Query, SemanticIntentConfidence.Low);

        var actionCount = 0;
        var queryCount = 0;
        var highConfidenceMatches = 0;

        foreach (var verb in verbs)
        {
            if (VerbDictionary.TryGetValue(verb, out var intent))
            {
                highConfidenceMatches++;
                if (intent == SemanticIntentKind.Action)
                    actionCount++;
                else
                    queryCount++;
            }
            else if (ActionVerbPrefixes.Any(prefix => verb.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                actionCount++;
            }
            else if (QueryVerbPrefixes.Any(prefix => verb.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                queryCount++;
            }
        }

        if (highConfidenceMatches == 0)
            return (SemanticIntentKind.Query, SemanticIntentConfidence.Low);

        var confidence = highConfidenceMatches switch
        {
            >= 2 when actionCount > 0 && queryCount == 0 => SemanticIntentConfidence.High,
            >= 2 when queryCount > 0 && actionCount == 0 => SemanticIntentConfidence.High,
            >= 2 when actionCount > 0 && queryCount > 0 => SemanticIntentConfidence.Medium,
            1 when actionCount > 0 => SemanticIntentConfidence.Medium,
            1 when queryCount > 0 => SemanticIntentConfidence.Low,
            _ => SemanticIntentConfidence.Low
        };

        var dominantIntent = actionCount >= queryCount ? SemanticIntentKind.Action : SemanticIntentKind.Query;

        return (dominantIntent, confidence);
    }

    private static List<string> ExtractVerbs(string normalizedQuery)
    {
        var verbs = new List<string>();
        var tokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];

            if (VerbDictionary.ContainsKey(token))
            {
                verbs.Add(token);
                continue;
            }

            if (i < tokens.Length - 1)
            {
                var twoWord = $"{token} {tokens[i + 1]}";
                if (VerbDictionary.ContainsKey(twoWord))
                {
                    verbs.Add(twoWord);
                    continue;
                }
            }

            if (i < tokens.Length - 2)
            {
                var threeWord = $"{token} {tokens[i + 1]} {tokens[i + 2]}";
                if (VerbDictionary.ContainsKey(threeWord))
                {
                    verbs.Add(threeWord);
                }
            }
        }

        return verbs;
    }
}