namespace FinFlow.Application.Chat.Services;

internal static class ScopeKeywords
{
    private static readonly string[] TenantScopePatterns =
    [
        "toan cong ty",
        "cong ty",
        "tenant",
        "workspace",
        "all company"
    ];

    private static readonly string[] DepartmentScopePatterns =
    [
        "phong ban",
        "department",
        "team"
    ];

    private static readonly string[] OwnScopePatterns =
    [
        "cua toi",
        "cua em",
        "cua minh",
        "my"
    ];

    private static readonly string[] OwnedDepartmentScopePatterns =
    [
        "team toi",
        "phong ban toi",
        "bo phan toi",
        "my team"
    ];

    private static readonly string[] CrossScopePatterns =
    [
        "nguoi khac",
        "dong nghiep",
        "others",
        "other people",
        "ngoai toi",
        "team kia",
        "phong khac",
        "bo phan khac",
        "khac",
        "so voi",
        "versus"
    ];

    private static readonly string[] GenericGroupScopePatterns =
    [
        "team nao",
        "phong nao",
        "bo phan nao",
        "department nao"
    ];

    public static bool MentionsTenantScope(string normalizedQuery)
    {
        foreach (var pattern in TenantScopePatterns)
        {
            if (normalizedQuery.Contains(pattern, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public static bool MentionsDepartmentScope(string normalizedQuery)
    {
        foreach (var pattern in DepartmentScopePatterns)
        {
            if (normalizedQuery.Contains(pattern, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public static bool MentionsOwnScope(string normalizedQuery)
    {
        foreach (var pattern in OwnScopePatterns)
        {
            if (normalizedQuery.Contains(pattern, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public static bool MentionsOwnedDepartmentScope(string normalizedQuery)
    {
        foreach (var pattern in OwnedDepartmentScopePatterns)
        {
            if (normalizedQuery.Contains(pattern, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public static bool MentionsCrossScope(string normalizedQuery)
    {
        foreach (var pattern in CrossScopePatterns)
        {
            if (normalizedQuery.Contains(pattern, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public static bool MentionsGenericGroupScope(string normalizedQuery)
    {
        foreach (var pattern in GenericGroupScopePatterns)
        {
            if (normalizedQuery.Contains(pattern, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}