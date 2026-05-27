using FinFlow.Application.Chat.Services;
using FinFlow.Domain.Chat;

namespace FinFlow.Application.Chat.Interfaces;

public interface IContextResolver
{
    Task<ContextResolutionResult> ResolveAsync(
        string query,
        IReadOnlyList<ChatMessage> history,
        ConversationContext? context,
        CancellationToken ct = default);

    Task<bool> IsFollowUpAsync(string query, IReadOnlyList<ChatMessage> history, CancellationToken ct = default);

    void RecordClarificationForAnalytics(Guid sessionId, string prompt, ClarificationReason reason, string originalQuery);
}

public class ContextResolutionResult
{
    public string ResolvedQuery { get; init; } = string.Empty;
    public float Confidence { get; init; }
    public ConfidenceLevel Level { get; init; }
    public List<EntityResolution> Resolutions { get; init; } = [];
    public bool RequiresClarification { get; init; }
    public string? ClarificationPrompt { get; init; }
    public ClarificationReason? ClarificationReason { get; init; }
    public bool CacheHit { get; init; }
}

public enum ClarificationReason
{
    NoContextAvailable,
    EntityNotFound,
    IntentUnclear,
    AmbiguousReference,
    LowConfidenceResolution
}

public enum ConfidenceLevel
{
    Low,      // < 0.60
    Medium,   // 0.60 - 0.84
    High      // >= 0.85
}

public class EntityResolution
{
    public string Original { get; init; } = string.Empty;
    public string Resolved { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public float Confidence { get; init; }
}

public interface IConfidenceScorer
{
    ConfidenceScore CalculateScore(
        float intentScore,
        float entityScore,
        float contextScore,
        float historyScore,
        float domainScore);

    ConfidenceLevel GetLevel(float confidence);
    string GetAction(ConfidenceLevel level);
}

public class ConfidenceScore
{
    public float Total { get; init; }
    public ConfidenceLevel Level { get; init; }
    public Dictionary<string, float> Factors { get; init; } = [];
}

public interface IHybridResolutionRouter
{
    Task<ResolutionResult> RouteAsync(
        string query,
        IReadOnlyList<ChatMessage> history,
        ConversationContext? context,
        CancellationToken ct = default);
}

public class ResolutionResult
{
    public string ResolvedQuery { get; init; } = string.Empty;
    public float Confidence { get; init; }
    public ResolutionTier Tier { get; init; }
    public bool RequiresClarification { get; init; }
    public string? ClarificationPrompt { get; init; }
}

public enum ResolutionTier
{
    Pattern,      // Fast pattern match - free
    Cache,        // Semantic cache hit - cheap
    SmallLlm,     // Small LLM resolution - moderate cost
    LargeLlm      // Large LLM fallback - expensive
}

public interface IConversationMemoryService
{
    Task<string?> GetMemoryAsync(Guid membershipId, CancellationToken ct);
    Task SetMemoryAsync(Guid membershipId, string memory, CancellationToken ct);
    Task ClearMemoryAsync(Guid membershipId, CancellationToken ct);
}

public interface IContextSummarizationService
{
    Task<string> SummarizeAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);

    Task<bool> ShouldSummarizeAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);
}