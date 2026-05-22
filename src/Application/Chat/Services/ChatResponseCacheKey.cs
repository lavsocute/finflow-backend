using System.Security.Cryptography;
using System.Text;
using FinFlow.Domain.Documents;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Build deterministic cache keys for chat responses, scoped by tenant and access scope
/// to prevent cross-scope cache poisoning.
/// </summary>
public static class ChatResponseCacheKey
{
    private const string Prefix = "chat:response:";

    /// <summary>
    /// Words that imply time-relative answers ("this month", "today") and therefore
    /// must not be cached because the answer changes over time.
    /// </summary>
    private static readonly string[] TimeRelativeMarkers =
    [
        "today", "yesterday", "tomorrow",
        "this month", "this year", "this quarter", "this week",
        "last month", "last year", "last week", "last quarter",
        "current month", "now", "current year",
        // Vietnamese
        "hôm nay", "hôm qua", "tháng này", "năm nay", "tuần này", "quý này",
        "tháng trước", "năm trước", "tuần trước"
    ];

    public static string Build(
        Guid tenantId,
        Guid membershipId,
        string role,
        Guid? departmentId,
        Guid? ownerFilter,
        IReadOnlyCollection<DocumentChunkType> allowedTypes,
        string query,
        string promptVersion)
    {
        var typesStr = string.Join(",", allowedTypes.OrderBy(t => t.ToString(), StringComparer.Ordinal).Select(t => t.ToString()));
        var normalized = NormalizeQueryStructure(query);
        var wordCount = GetWordCount(query);
        // Include membershipId to prevent cross-user cache poisoning
        // Include normalized query structure + word count to prevent collision from semantically different queries
        var keyInput = $"{promptVersion}|{membershipId}|{role}|{departmentId}|{ownerFilter}|{typesStr}|{wordCount}|{normalized}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(keyInput));
        var hex = Convert.ToHexString(hash);
        return $"{Prefix}{tenantId}:{hex}";
    }

    /// <summary>
    /// Normalize query by stripping content words but preserving structural pattern.
    /// This prevents "show expenses" and "show expenses this month" from colliding.
    /// </summary>
    private static string NormalizeQueryStructure(string query)
    {
        var trimmed = query.Trim().ToLowerInvariant();
        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return string.Empty;
        // Keep first and last word as anchors, mark middle section size
        if (words.Length == 1) return words[0];
        if (words.Length == 2) return $"{words[0]}|_|{words[1]}";
        return $"{words[0]}|{words.Length - 2} words|{words[^1]}";
    }

    private static int GetWordCount(string query)
    {
        return query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    /// Per-tenant invalidation prefix: when a document is reindexed/deindexed,
    /// the consumer should call cache invalidation on this prefix to wipe all cached responses.
    /// </summary>
    public static string TenantInvalidationKey(Guid tenantId) => $"{Prefix}{tenantId}:";

    public static bool IsCacheable(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var wordCount = GetWordCount(query);
        // Reject single-word or very short queries from caching
        if (wordCount < 3) return false;
        var lower = query.ToLowerInvariant();
        return !TimeRelativeMarkers.Any(marker => lower.Contains(marker));
    }
}

/// <summary>
/// Cached chat response payload. Stored after a successful uncached request,
/// returned directly on cache hit.
/// </summary>
public sealed record ChatResponseCacheEntry(
    string Answer,
    int DocumentCount,
    int TokenUsage,
    IReadOnlyList<CachedCitation> Citations);

public sealed record CachedCitation(
    int ChunkNumber,
    Guid ChunkId,
    Guid DocumentId,
    string ChunkType,
    string Preview);
