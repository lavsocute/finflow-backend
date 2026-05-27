using System.Text;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Chat;
using Microsoft.Extensions.Logging;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Resolves entity references and follow-up queries using LLM extraction and context matching.
/// </summary>
public sealed class ContextResolver : IContextResolver
{
    private readonly IConfidenceScorer _confidenceScorer;
    private readonly ILlmEntityExtractor _llmExtractor;
    private readonly ILogger<ContextResolver> _logger;
    private readonly ITextNormalizer _textNormalizer;

    // Fallback patterns when LLM is disabled
    private static readonly string[] ComparativePatterns =
    [
        "hơn", "kém", "bằng"
    ];

    private static readonly string[] QuestionWords =
    [
        "ai", "gì", "đâu", "nào", "sao", "thế nào", "như nào", "ra sao", "bao nhiêu", "mấy"
    ];

    private static readonly string[] OwnReferenceWords =
        ["tôi", "của tôi", "em", "của em", "minh", "của minh", "của mình"];

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "và", "thì", "có", "không", "theo", "với", "cho", "để", "của", "trong",
        "là", "một", "các", "được", "này", "kia", "đó", "bên"
    };

    private static readonly HashSet<string> SharedQueryTokens = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<(string Pattern, ClarificationReason Reason)> _entityClarificationPatterns =
    [
        ("phòng nào", ClarificationReason.EntityNotFound),
        ("bên nào", ClarificationReason.EntityNotFound),
        ("ai", ClarificationReason.EntityNotFound),
        ("đối tượng nào", ClarificationReason.EntityNotFound),
        ("thứ gì", ClarificationReason.EntityNotFound)
    ];

    public ContextResolver(
        IConfidenceScorer confidenceScorer,
        ILogger<ContextResolver> logger)
        : this(confidenceScorer, NullLlmEntityExtractor.Instance, logger, new TextNormalizer())
    {
    }

    public ContextResolver(
        IConfidenceScorer confidenceScorer,
        ILlmEntityExtractor llmExtractor,
        ILogger<ContextResolver> logger,
        ITextNormalizer textNormalizer)
    {
        _confidenceScorer = confidenceScorer;
        _llmExtractor = llmExtractor;
        _logger = logger;
        _textNormalizer = textNormalizer;
    }

    public async Task<ContextResolutionResult> ResolveAsync(
        string query,
        IReadOnlyList<ChatMessage> history,
        ConversationContext? context,
        CancellationToken ct = default)
    {
        var normalizedQuery = _textNormalizer.Normalize(query);

        // Use LLM-based follow-up detection
        var followUpResult = await _llmExtractor.DetectFollowUpAsync(query, history, ct);
        var isPatternFollowUp = IsPatternFollowUp(normalizedQuery, history);

        if (!followUpResult.IsFollowUp && !isPatternFollowUp)
        {
            return new ContextResolutionResult
            {
                ResolvedQuery = query,
                Confidence = 1.0f,
                Level = ConfidenceLevel.High,
                RequiresClarification = false
            };
        }

        // Use LLM-based entity extraction
        var llmEntities = await _llmExtractor.ExtractEntitiesAsync(query, history, ct);

        // Try to resolve references from context using LLM
        var resolutions = new List<EntityResolution>();
        var totalConfidence = 0f;

        // FALLBACK STRATEGY 1: Try to get from conversation history if no context
        if (context == null && history.Count > 0)
        {
            context = BuildContextFromHistory(history);
        }

        if (context != null && llmEntities.Count > 0)
        {
            // Use LLM-based entity resolution
            var llmResolutions = await _llmExtractor.ResolveEntityReferencesAsync(query, context, llmEntities, history, ct);
            foreach (var resolution in llmResolutions)
            {
                resolutions.Add(resolution);
                totalConfidence += resolution.Confidence;
            }
        }

        if (context != null && resolutions.Count == 0)
        {
            resolutions.AddRange(ResolveReferencesFromContext(normalizedQuery, context));
            totalConfidence += resolutions.Sum(static resolution => resolution.Confidence);
        }

        // Apply all replacements using StringBuilder to avoid multiple string allocations
        var avgConfidence = resolutions.Count > 0
            ? totalConfidence / resolutions.Count
            : isPatternFollowUp && context?.GetActiveEntities().Count > 0 ? 1.0f
            : isPatternFollowUp ? 0.7f
            : followUpResult.Confidence;

        var confidence = CalculateFollowUpConfidence(resolutions.Count, history.Count, avgConfidence);
        var level = _confidenceScorer.GetLevel(confidence);
        var resolvedQueryBuilder = new StringBuilder(query);
        foreach (var resolution in resolutions)
        {
            ReplaceOrdinalIgnoreCase(resolvedQueryBuilder, resolution.Original, resolution.Resolved);
        }
        var resolvedQuery = resolvedQueryBuilder.ToString();

        // Determine clarification reason for analytics
        var clarificationReason = DetermineClarificationReason(normalizedQuery, context, resolutions, level);

        return new ContextResolutionResult
        {
            ResolvedQuery = resolvedQuery,
            Confidence = confidence,
            Level = level,
            Resolutions = resolutions,
            RequiresClarification = level == ConfidenceLevel.Low,
            ClarificationPrompt = level == ConfidenceLevel.Low
                ? GenerateClarificationPrompt(resolvedQuery, context, clarificationReason)
                : null,
            ClarificationReason = level == ConfidenceLevel.Low ? clarificationReason : null
        };
    }

    public async Task<bool> IsFollowUpAsync(
        string query,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default)
    {
        if (history.Count < 1)
            return false;

        // Use LLM-based follow-up detection
        var result = await _llmExtractor.DetectFollowUpAsync(query, history, ct);
        return result.IsFollowUp || IsPatternFollowUp(_textNormalizer.Normalize(query), history);
    }

    public void RecordClarificationForAnalytics(Guid sessionId, string prompt, ClarificationReason reason, string originalQuery)
    {
        _logger.LogInformation(
            "Clarification prompt recorded for analytics. SessionId: {SessionId}, Reason: {Reason}, Prompt: {Prompt}, OriginalQuery: {OriginalQuery}",
            sessionId,
            reason,
            prompt,
            originalQuery);
    }

    private float CalculateEntityOverlap(string normalizedQuery, IReadOnlyList<ChatMessage> history)
    {
        if (history.Count == 0)
            return 0f;

        SharedQueryTokens.Clear();
        SharedQueryTokens.UnionWith(normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        SharedQueryTokens.ExceptWith(StopWords);

        var overlapCount = 0;
        var totalTokens = 0;

        // Check last 3 messages for entity overlap
        var recentMessages = history.TakeLast(3);
        foreach (var message in recentMessages)
        {
            var normalizedMsg = IntentTextNormalizer.Normalize(message.Content);
            var msgTokens = normalizedMsg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in msgTokens)
            {
                if (!StopWords.Contains(token))
                {
                    totalTokens++;
                    if (SharedQueryTokens.Contains(token))
                        overlapCount++;
                }
            }
        }

        return totalTokens > 0 ? (float)overlapCount / totalTokens : 0f;
    }

    private EntityResolution? ResolveEntityReference(ExtractedEntity entity, ConversationContext context)
    {
        // Try to find matching entity in context by canonical name
        var matched = context.FindEntity(entity.Text);
        if (matched != null)
        {
            context.AddAliasToEntity(matched.Id, entity.Text);
            matched.AddAlias(entity.Text);

            return new EntityResolution
            {
                Original = entity.Text,
                Resolved = matched.CanonicalName,
                Source = "context",
                Confidence = 0.85f * (float)entity.Confidence
            };
        }

        // Try to find entity by type
        var byType = context.FindEntityByType(entity.Type);
        if (byType != null)
        {
            context.AddAliasToEntity(byType.Id, entity.Text);
            byType.AddAlias(entity.Text);

            return new EntityResolution
            {
                Original = entity.Text,
                Resolved = byType.CanonicalName,
                Source = $"context_{entity.Type.ToString().ToLowerInvariant()}",
                Confidence = 0.75f * (float)entity.Confidence
            };
        }

        return null;
    }

    private float CalculateFollowUpConfidence(int resolutionCount, int historyCount, float entityConfidence)
    {
        // More resolutions found = higher confidence
        var resolutionBonus = Math.Min(resolutionCount * 0.15f, 0.4f);

        // More history = more context but also more noise
        var historyFactor = historyCount switch
        {
            <= 3 => 0.2f,
            <= 10 => 0.15f,
            _ => 0.1f
        };

        var baseScore = 0.4f;
        return Math.Min(baseScore + resolutionBonus + historyFactor + (entityConfidence * 0.25f), 1.0f);
    }

    private ClarificationReason DetermineClarificationReason(
        string normalizedQuery,
        ConversationContext? context,
        List<EntityResolution> resolutions,
        ConfidenceLevel level)
    {
        // Check for entity clarification patterns first
        foreach (var (pattern, reason) in _entityClarificationPatterns)
        {
            if (normalizedQuery.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return reason;
            }
        }

        // Check if no context is available
        if (context == null || context.Entities.Count == 0)
        {
            return ClarificationReason.NoContextAvailable;
        }

        // Check if entity was not found
        if (resolutions.Count == 0 && HasEntityReference(normalizedQuery))
        {
            return ClarificationReason.EntityNotFound;
        }

        // Check if intent is unclear (very short query, ambiguous)
        var tokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length <= 2)
        {
            return ClarificationReason.IntentUnclear;
        }

        // Check for ambiguous references
        if (normalizedQuery.Contains("đó") || normalizedQuery.Contains("kia") || normalizedQuery.Contains("nào"))
        {
            return ClarificationReason.AmbiguousReference;
        }

        // Fall back to low confidence resolution
        if (level == ConfidenceLevel.Low)
        {
            return ClarificationReason.LowConfidenceResolution;
        }

        return ClarificationReason.IntentUnclear;
    }

    private bool HasEntityReference(string normalizedQuery)
    {
        return QuestionWords.Any(qw => normalizedQuery.Contains(qw, StringComparison.OrdinalIgnoreCase)) ||
               OwnReferenceWords.Any(ow => normalizedQuery.Contains(ow, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPatternFollowUp(string normalizedQuery, IReadOnlyList<ChatMessage> history)
    {
        if (history.Count == 0)
            return false;

        return normalizedQuery.StartsWith("con ", StringComparison.Ordinal) ||
               normalizedQuery.Contains(" thi sao", StringComparison.Ordinal) ||
               normalizedQuery.Contains("so sanh", StringComparison.Ordinal) ||
               normalizedQuery.Contains("hon", StringComparison.Ordinal) ||
               normalizedQuery.Contains("kem", StringComparison.Ordinal);
    }

    private static IReadOnlyList<EntityResolution> ResolveReferencesFromContext(
        string normalizedQuery,
        ConversationContext context)
    {
        var resolutions = new List<EntityResolution>();
        foreach (var entity in context.GetActiveEntities())
        {
            if (entity.Type != EntityType.DEPARTMENT)
                continue;

            var normalizedName = IntentTextNormalizer.Normalize(entity.CanonicalName);
            var significantTokens = normalizedName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => token.Length > 0 && token is not "phong" and not "ban" and not "bo" and not "phan")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var token in significantTokens)
            {
                if (!ContainsWholeToken(normalizedQuery, token))
                    continue;

                resolutions.Add(new EntityResolution
                {
                    Original = token,
                    Resolved = entity.CanonicalName,
                    Source = "context_department_token",
                    Confidence = 0.8f
                });
                break;
            }
        }

        return resolutions;
    }

    private static bool ContainsWholeToken(string normalizedQuery, string token)
    {
        var searchable = normalizedQuery
            .Replace("?", " ", StringComparison.Ordinal)
            .Replace(",", " ", StringComparison.Ordinal)
            .Replace(".", " ", StringComparison.Ordinal)
            .Replace("!", " ", StringComparison.Ordinal);
        var paddedQuery = $" {searchable} ";
        var paddedToken = $" {token} ";
        return paddedQuery.Contains(paddedToken, StringComparison.OrdinalIgnoreCase);
    }

    private static void ReplaceOrdinalIgnoreCase(StringBuilder builder, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(oldValue))
            return;

        var current = builder.ToString();
        var index = current.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            builder.Remove(index, oldValue.Length);
            builder.Insert(index, newValue);
            current = builder.ToString();
            index = current.IndexOf(oldValue, index + newValue.Length, StringComparison.OrdinalIgnoreCase);
        }
    }

    private string GenerateClarificationPrompt(string query, ConversationContext? context, ClarificationReason reason)
    {
        return reason switch
        {
            ClarificationReason.NoContextAvailable => GenerateNoContextPrompt(query),
            ClarificationReason.EntityNotFound => GenerateEntityNotFoundPrompt(query, context),
            ClarificationReason.IntentUnclear => GenerateIntentUnclearPrompt(query),
            ClarificationReason.AmbiguousReference => GenerateAmbiguousPrompt(query, context),
            ClarificationReason.LowConfidenceResolution => GenerateLowConfidencePrompt(query, context),
            _ => "Bạn có thể cho biết rõ hơn bạn đang hỏi về đối tượng nào không?"
        };
    }

    private string GenerateNoContextPrompt(string query)
    {
        if (OwnReferenceWords.Any(ow => query.Contains(ow, StringComparison.OrdinalIgnoreCase)))
        {
            return "Tôi thấy bạn đề cập đến vấn đề cá nhân, nhưng tôi chưa rõ bạn đang nói về vấn đề gì. Bạn có thể cho biết thêm chi tiết không?";
        }

        return "Tôi chưa hiểu rõ ý bạn. Bạn đang hỏi về đối tượng nào? Xin lỗi, tôi không hiểu bạn đang nói về gì. Bạn có thể cho biết rõ hơn?";
    }

    private string GenerateEntityNotFoundPrompt(string query, ConversationContext? context)
    {
        if (context?.Entities.Count > 0)
        {
            var entities = context.Entities.Take(3).Select(e => e.CanonicalName).ToList();
            var entityList = string.Join(", ", entities);
            return $"Tôi thấy bạn đề cập đến vấn đề này, nhưng chưa rõ là đối tượng nào. Bạn đang nói về: {entityList} phải không?";
        }

        return "Bạn đang hỏi về đối tượng nào? Tôi có thể giúp bạn nếu bạn nói rõ hơn.";
    }

    private string GenerateIntentUnclearPrompt(string query)
    {
        if (ComparativePatterns.Any(cp => query.Contains(cp, StringComparison.OrdinalIgnoreCase)))
        {
            return "Bạn muốn so sánh những đối tượng nào với nhau? Xin cho tôi biết thêm chi tiết.";
        }

        if (OwnReferenceWords.Any(ow => query.Contains(ow, StringComparison.OrdinalIgnoreCase)))
        {
            return "Tôi thấy bạn đề cập đến vấn đề cá nhân, nhưng tôi chưa hiểu rõ bạn muốn hỏi về điều gì. Bạn có thể cho biết cụ thể hơn không?";
        }

        return "Xin lỗi, tôi không hiểu bạn đang nói về gì. Bạn có thể cho biết rõ hơn không? Ví dụ: \"Chi phí của phòng nhân sự\" hoặc \"Báo cáo tháng này\".";
    }

    private string GenerateAmbiguousPrompt(string query, ConversationContext? context)
    {
        var department = context?.FindEntityByType(EntityType.DEPARTMENT);
        if (department != null)
        {
            return $"Bạn đang hỏi về \"{query}\" - có phải bạn đang nói về {department.CanonicalName} không?";
        }

        return $"Tôi thấy bạn đề cập đến \"{query}\", nhưng tôi chưa rõ đối tượng cụ thể. Bạn có thể cho biết rõ hơn không?";
    }

    private string GenerateLowConfidencePrompt(string query, ConversationContext? context)
    {
        var department = context?.FindEntityByType(EntityType.DEPARTMENT);
        if (department != null)
        {
            return $"Bạn đang hỏi về \"{query}\" - có phải bạn đang nói về {department.CanonicalName} không? Nếu không, xin cho tôi biết phòng ban cụ thể.";
        }

        return "Bạn có thể cho biết rõ hơn bạn đang hỏi về đối tượng nào không? Tôi muốn đảm bảo tôi trả lời đúng câu hỏi của bạn.";
    }

    /// <summary>
    /// FALLBACK STRATEGY: Build context from conversation history when no context is available
    /// </summary>
    private ConversationContext? BuildContextFromHistory(IReadOnlyList<ChatMessage> history)
    {
        if (history.Count == 0)
            return null;

        var sessionId = history.LastOrDefault()?.SessionId ?? Guid.NewGuid();
        var context = ConversationContext.Create(sessionId);

        // Extract entities from recent messages (last 5)
        var recentMessages = history.TakeLast(5);
        var turnNumber = 1;

        foreach (var message in recentMessages)
        {
            var normalized = IntentTextNormalizer.Normalize(message.Content);
            var extractedEntities = ExtractEntitiesFromQuery(normalized);

            foreach (var entity in extractedEntities)
            {
                if (context.FindEntity(entity.Text) == null)
                {
                    var trackedEntity = TrackedEntity.Create(entity.Text, entity.Type, turnNumber);
                    context.AddEntity(trackedEntity);
                }
            }

            turnNumber++;
        }

        return context;
    }

    /// <summary>
    /// TODO: [DRY VIOLATION] Entity extraction logic is duplicated in ContextRecoveryService.
    /// ExtractDepartmentsFromTokens, ExtractMoneyFromTokens, ExtractDatesFromTokens (lines 192-325)
    /// share similar patterns with this method. Consider extracting to a shared IEntityExtractor service.
    /// Note: This method returns ExtractedEntity while ContextRecoveryService returns TrackedEntity.
    /// </summary>
    private List<ExtractedEntity> ExtractEntitiesFromQuery(string normalizedQuery)
    {
        var entities = new List<ExtractedEntity>();
        var tokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var departmentPrefixes = new[] { "phòng", "ban", "bộ phận", "tổ", "team", "nhóm" };
        var personPrefixes = new[] { "ông", "bà", "anh", "chị", "bạn", "thầy", "cô" };
        var moneySuffixes = new[] { "đồng", "k", "nghìn", "triệu", "tỷ", "USD", "VND", "dollar" };
        var dateKeywords = new[] { "ngày", "tháng", "năm", "hôm nay", "hôm qua", "ngày mai", "tuần này", "tháng này", "quý" };

        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i].ToLowerInvariant();

            // Detect department names
            if (departmentPrefixes.Any(dp => token.Contains(dp)))
            {
                var departmentTokens = new List<string> { tokens[i] };
                for (int j = i + 1; j < Math.Min(i + 3, tokens.Length); j++)
                {
                    if (StopWords.Contains(tokens[j]))
                        break;
                    departmentTokens.Add(tokens[j]);
                    if (!departmentPrefixes.Any(dp => tokens[j].ToLowerInvariant().Contains(dp)))
                        break;
                }

                if (departmentTokens.Count > 1)
                {
                    entities.Add(new ExtractedEntity(
                        string.Join(" ", departmentTokens),
                        EntityType.DEPARTMENT,
                        0.9));
                }
            }
            // Detect person names
            else if (personPrefixes.Contains(token))
            {
                var personTokens = new List<string> { tokens[i] };
                for (int j = i + 1; j < Math.Min(i + 3, tokens.Length); j++)
                {
                    if (StopWords.Contains(tokens[j]))
                        break;
                    personTokens.Add(tokens[j]);
                    if (personPrefixes.Any(pp => tokens[j].ToLowerInvariant().Contains(pp)))
                        break;
                }

                if (personTokens.Count > 1)
                {
                    entities.Add(new ExtractedEntity
                    {
                        Text = string.Join(" ", personTokens),
                        Type = EntityType.PERSON,
                        Confidence = 0.85f
                    });
                }
            }
            // Detect money amounts
            else if (moneySuffixes.Any(ms => token.Contains(ms)))
            {
                var moneyTokens = new List<string>();
                for (int j = Math.Max(0, i - 3); j <= i; j++)
                {
                    if (StopWords.Contains(tokens[j]))
                        continue;
                    if (IsNumeric(tokens[j]))
                        moneyTokens.Add(tokens[j]);
                }
                moneyTokens.Add(tokens[i]);

                entities.Add(new ExtractedEntity
                {
                    Text = string.Join(" ", moneyTokens),
                    Type = EntityType.MONEY,
                    Confidence = 0.9f
                });
            }
            // Detect dates
            else if (dateKeywords.Any(dk => token.Contains(dk)))
            {
                var dateTokens = new List<string> { tokens[i] };
                for (int j = i + 1; j < Math.Min(i + 4, tokens.Length); j++)
                {
                    if (StopWords.Contains(tokens[j]) || IsNumeric(tokens[j]))
                    {
                        dateTokens.Add(tokens[j]);
                    }
                    else if (dateKeywords.Any(dk => tokens[j].ToLowerInvariant().Contains(dk)))
                    {
                        dateTokens.Add(tokens[j]);
                    }
                    else
                    {
                        break;
                    }
                }

                if (dateTokens.Count >= 1)
                {
                    entities.Add(new ExtractedEntity
                    {
                        Text = string.Join(" ", dateTokens),
                        Type = EntityType.DATE,
                        Confidence = 0.85f
                    });
                }
            }
        }

        return entities;
    }

    private static bool IsNumeric(string token)
    {
        return double.TryParse(token, out _) ||
               token.EndsWith("k", StringComparison.OrdinalIgnoreCase) ||
               token.EndsWith("nghìn", StringComparison.OrdinalIgnoreCase) ||
               token.EndsWith("triệu", StringComparison.OrdinalIgnoreCase) ||
               token.EndsWith("tỷ", StringComparison.OrdinalIgnoreCase);
    }
}
