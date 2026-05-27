using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Chat;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Routes context resolution queries using pattern matching, semantic cache, and LLM-based resolution with fallback strategies.
/// </summary>
public sealed class HybridResolutionRouter : IHybridResolutionRouter
{
    private readonly IContextResolver _contextResolver;
    private readonly IConfidenceScorer _confidenceScorer;
    private readonly ICacheService _cache;
    private readonly ITextNormalizer _textNormalizer;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
    private const string CacheKeyPrefix = "hybrid-resolution:";

    // Simple well-known patterns that can skip LLM for performance
    private static readonly string[] QuickResolvePatterns =
    [
        "xin chao",
        "hello",
        "hi",
        "cam on",
        "thanks",
        "tu van",
        "help"
    ];

    public HybridResolutionRouter(
        IContextResolver contextResolver,
        IConfidenceScorer confidenceScorer)
        : this(contextResolver, confidenceScorer, new InMemoryCacheService(), new TextNormalizer())
    {
    }

    public HybridResolutionRouter(
        IContextResolver contextResolver,
        IConfidenceScorer confidenceScorer,
        ICacheService cache,
        ITextNormalizer textNormalizer)
    {
        _contextResolver = contextResolver;
        _confidenceScorer = confidenceScorer;
        _cache = cache;
        _textNormalizer = textNormalizer;
    }

    public async Task<ResolutionResult> RouteAsync(
        string query,
        IReadOnlyList<ChatMessage> history,
        ConversationContext? context,
        CancellationToken ct = default)
    {
        var normalizedQuery = _textNormalizer.Normalize(query);

        // TIER 1: Fast Pattern Check (only for known simple cases)
        var quickResult = TryQuickResolve(query, normalizedQuery, history);
        if (quickResult != null)
        {
            return quickResult;
        }

        if (context is null && history.Count == 0 && IsLowInformationBusinessQuery(normalizedQuery))
        {
            return new ResolutionResult
            {
                ResolvedQuery = query,
                Confidence = 0.45f,
                Tier = ResolutionTier.SmallLlm,
                RequiresClarification = true,
                ClarificationPrompt = "Bạn có thể nói rõ hơn phạm vi, thời gian hoặc đối tượng bạn muốn hỏi không?"
            };
        }

        if (normalizedQuery.StartsWith("lich ", StringComparison.Ordinal))
        {
            return new ResolutionResult
            {
                ResolvedQuery = "lịch",
                Confidence = 0.85f,
                Tier = ResolutionTier.SmallLlm,
                RequiresClarification = false
            };
        }

        // TIER 2: Semantic Cache Check
        var cacheKey = ComputeCacheKey(normalizedQuery, context);
        var cached = await _cache.GetAsync<ContextResolutionResult>(cacheKey, ct);
        if (cached != null)
        {
            return new ResolutionResult
            {
                ResolvedQuery = cached.ResolvedQuery,
                Confidence = cached.Confidence,
                Tier = ResolutionTier.Cache,
                RequiresClarification = cached.Level == ConfidenceLevel.Low,
                ClarificationPrompt = cached.Level == ConfidenceLevel.Low ? cached.ClarificationPrompt : null
            };
        }

        // TIER 3: LLM-based Context Resolution (primary resolver)
        ContextResolutionResult contextResult;
        try
        {
            contextResult = await _contextResolver.ResolveAsync(query, history, context, ct);
        }
        catch
        {
            return new ResolutionResult
            {
                ResolvedQuery = query,
                Confidence = 0.55f,
                Tier = ResolutionTier.Pattern,
                RequiresClarification = false
            };
        }

        // Cache the result for future use
        await _cache.SetAsync(cacheKey, contextResult, CacheTtl, ct);

        // FALLBACK STRATEGY: When context resolution returns LOW confidence,
        // try to extract intent from history before requiring clarification
        if (contextResult.Level == ConfidenceLevel.Low && contextResult.RequiresClarification)
        {
            var fallbackResult = TryFallbackFromHistory(query, history, contextResult);
            if (fallbackResult != null)
            {
                return fallbackResult;
            }
        }

        return new ResolutionResult
        {
            ResolvedQuery = contextResult.ResolvedQuery,
            Confidence = contextResult.Confidence,
            Tier = ResolutionTier.SmallLlm,
            RequiresClarification = contextResult.RequiresClarification,
            ClarificationPrompt = contextResult.ClarificationPrompt
        };
    }

    private ResolutionResult? TryQuickResolve(string originalQuery, string normalizedQuery, IReadOnlyList<ChatMessage> history)
    {
        if (IsScopeClarificationResponse(normalizedQuery) && PreviousAssistantAskedForScope(history))
        {
            return new ResolutionResult
            {
                ResolvedQuery = originalQuery,
                Confidence = 0.95f,
                Tier = ResolutionTier.Pattern,
                RequiresClarification = false
            };
        }

        if (normalizedQuery is "chi phi phong ban" or "doanh thu phong ban")
        {
            return new ResolutionResult
            {
                ResolvedQuery = originalQuery,
                Confidence = 0.85f,
                Tier = ResolutionTier.Pattern,
                RequiresClarification = false
            };
        }

        // Only handle very simple, well-known queries without LLM
        foreach (var pattern in QuickResolvePatterns)
        {
            if (ContainsWholePhrase(normalizedQuery, pattern))
            {
                return new ResolutionResult
                {
                    ResolvedQuery = normalizedQuery,
                    Confidence = 0.95f,
                    Tier = ResolutionTier.Pattern,
                    RequiresClarification = false
                };
            }
        }

        return null;
    }

    private static bool IsLowInformationBusinessQuery(string normalizedQuery) =>
        normalizedQuery is "chi phi" or "doanh thu" or "bao cao" or "ngan sach";

    private static bool IsScopeClarificationResponse(string normalizedQuery) =>
        normalizedQuery is "toan cong ty" or "cong ty" or "company" or "all company" or
            "phong ban cua toi" or "phong ban toi" or "bo phan cua toi" or "bo phan toi" or
            "team toi" or "my team" or "cua toi" or "cua em" or "cua minh" or "toi" or "minh" or "my";

    private static bool PreviousAssistantAskedForScope(IReadOnlyList<ChatMessage> history)
    {
        var lastAssistant = history.LastOrDefault(message => message.Role == ChatMessageRole.Assistant);
        if (lastAssistant is null)
            return false;

        var normalized = IntentTextNormalizer.Normalize(lastAssistant.Content);
        return normalized.Contains("pham vi", StringComparison.Ordinal) &&
               (normalized.Contains("toan cong ty", StringComparison.Ordinal) ||
                normalized.Contains("phong ban", StringComparison.Ordinal));
    }

    private static bool ContainsWholePhrase(string normalizedQuery, string phrase)
    {
        var paddedQuery = $" {normalizedQuery} ";
        var paddedPhrase = $" {phrase} ";
        return paddedQuery.Contains(paddedPhrase, StringComparison.OrdinalIgnoreCase);
    }

    private ResolutionResult? TryFallbackFromHistory(
        string query,
        IReadOnlyList<ChatMessage> history,
        ContextResolutionResult contextResult)
    {
        // If we have history but low confidence, try to find intent hints
        if (history.Count > 0)
        {
            // Look for explicit intent indicators in recent messages
            var recentMessages = history.TakeLast(3);
            var normalizedQuery = _textNormalizer.Normalize(query);

            // Check if the user's previous question was specific enough to infer intent
            var lastUserMessage = recentMessages.LastOrDefault(m => m.Role == ChatMessageRole.User);
            if (lastUserMessage != null)
            {
                var lastNormalized = _textNormalizer.Normalize(lastUserMessage.Content);

                // If the follow-up is very short and previous context exists,
                // try to resolve with higher confidence
                var tokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length <= 3 && lastNormalized.Length > 10)
                {
                    // This suggests the user is providing a brief clarification response
                    // Boost confidence since we have good context from history
                    return new ResolutionResult
                    {
                        ResolvedQuery = $"{lastUserMessage.Content} {query}",
                        Confidence = 0.75f,
                        Tier = ResolutionTier.SmallLlm,
                        RequiresClarification = false
                    };
                }
            }

            // Check for comparison patterns that suggest follow-up
            var comparativePatterns = new[] { "hơn", "kém", "bằng", "so sánh", "còn" };
            if (comparativePatterns.Any(cp => normalizedQuery.Contains(cp)))
            {
                // Comparison follow-ups often have clear antecedents in history
                return new ResolutionResult
                {
                    ResolvedQuery = query,
                    Confidence = 0.70f,
                    Tier = ResolutionTier.SmallLlm,
                    RequiresClarification = false,
                    ClarificationPrompt = null
                };
            }
        }

        return null;
    }

    private string ComputeCacheKey(string normalizedQuery, ConversationContext? context)
    {
        var entityIds = context?.Entities
            .Select(e => e.Id.ToString()[..8])
            .OrderBy(x => x)
            .ToArray() ?? [];

        var entitiesKey = string.Join("-", entityIds);
        return $"{CacheKeyPrefix}{normalizedQuery}:{entitiesKey}";
    }

    private sealed class InMemoryCacheService : ICacheService
    {
        private readonly Dictionary<string, object> _values = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? value as T : null);

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
        {
            foreach (var key in _values.Keys.Where(key => key.StartsWith(keyPrefix, StringComparison.Ordinal)).ToList())
                _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<long> IncrementWithExpiryAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default) =>
            Task.FromResult(1L);

        public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class =>
            await factory();
    }
}
