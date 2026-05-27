using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Chat;
using Microsoft.Extensions.Logging;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Provides graceful degradation when LLM extraction fails or returns low confidence.
/// Falls back to conversation history analysis and natural clarification prompts in Vietnamese.
/// </summary>
public sealed class ContextRecoveryService : IContextRecoveryService
{
    private readonly IConversationStateManager _stateManager;
    private readonly ILogger<ContextRecoveryService> _logger;
    private readonly ITextNormalizer _textNormalizer;

    private const int DefaultHistoryMessageCount = 5;
    private const int MaxHistoryMessageCount = 10;

    public ContextRecoveryService(
        IConversationStateManager stateManager,
        ILogger<ContextRecoveryService> logger,
        ITextNormalizer? textNormalizer = null)
    {
        _stateManager = stateManager;
        _logger = logger;
        _textNormalizer = textNormalizer ?? new TextNormalizer();
    }

    /// <summary>
    /// Attempts to recover context from conversation history when LLM is unavailable or extraction failed.
    /// </summary>
    public async Task<ContextRecoveryResult> RecoverFromHistoryAsync(
        Guid sessionId,
        IReadOnlyList<ChatMessage> history,
        int messageCount = DefaultHistoryMessageCount,
        CancellationToken ct = default)
    {
        if (history.Count == 0)
        {
            return new ContextRecoveryResult
            {
                Success = false,
                RecoveryStrategy = RecoveryStrategy.None,
                Message = "Không có lịch sử hội thoại để phục hồi."
            };
        }

        var actualMessageCount = Math.Min(messageCount, MaxHistoryMessageCount);
        var recentMessages = history.TakeLast(actualMessageCount).ToList();

        // Extract entities and intent from recent messages
        var extractedContext = ExtractContextFromHistory(sessionId, recentMessages);

        if (extractedContext.Entities.Count == 0 && string.IsNullOrEmpty(extractedContext.InferredIntent))
        {
            _logger.LogDebug(
                "Context recovery found no entities or intent in last {Count} messages for session {SessionId}",
                actualMessageCount, sessionId);

            return new ContextRecoveryResult
            {
                Success = false,
                RecoveryStrategy = RecoveryStrategy.None,
                Message = "Không thể phục hồi ngữ cảnh từ lịch sử hội thoại."
            };
        }

        // Store recovered entities in conversation state
        foreach (var entity in extractedContext.Entities)
        {
            await _stateManager.AddEntityAsync(sessionId, entity, ct);
        }

        _logger.LogInformation(
            "Context recovery successful for session {SessionId}. Recovered {EntityCount} entities, inferred intent: {Intent}",
            sessionId, extractedContext.Entities.Count, extractedContext.InferredIntent ?? "unknown");

        return new ContextRecoveryResult
        {
            Success = true,
            RecoveryStrategy = RecoveryStrategy.HistoryAnalysis,
            RecoveredEntities = extractedContext.Entities,
            InferredIntent = extractedContext.InferredIntent,
            Message = "Đã phục hồi ngữ cảnh từ lịch sử hội thoại."
        };
    }

    /// <summary>
    /// Generates a natural Vietnamese clarification prompt when confidence is low or entity is unknown.
    /// </summary>
    public string GenerateClarificationPrompt(
        string query,
        ClarificationType type,
        IReadOnlyList<ChatMessage>? history = null)
    {
        return type switch
        {
            ClarificationType.LowConfidence => GenerateLowConfidencePrompt(query, history),
            ClarificationType.EntityUnknown => GenerateUnknownEntityPrompt(query, history),
            ClarificationType.IntentUnclear => GenerateUnclearIntentPrompt(query, history),
            ClarificationType.MultipleMatches => GenerateMultipleMatchesPrompt(query, history),
            ClarificationType.AmbiguousReference => GenerateAmbiguousReferencePrompt(query, history),
            _ => GenerateGenericClarificationPrompt(query)
        };
    }

    /// <summary>
    /// Analyzes conversation history to infer the user's intent when LLM extraction fails.
    /// </summary>
    public async Task<IntentInferenceResult> InferIntentFromHistoryAsync(
        Guid sessionId,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default)
    {
        if (history.Count == 0)
        {
            return new IntentInferenceResult
            {
                Success = false,
                InferredIntent = null,
                Confidence = 0f,
                Reasoning = "No history available"
            };
        }

        var recentMessages = history.TakeLast(5).ToList();
        var intentSignals = new Dictionary<string, int>();
        var topicSignals = new Dictionary<string, int>();

        foreach (var message in recentMessages)
        {
            if (message.Role != ChatMessageRole.User)
                continue;

            var normalized = _textNormalizer.Normalize(message.Content);
            var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Extract intent signals from keywords
            AnalyzeIntentKeywords(normalized, intentSignals);

            // Extract topic signals from noun phrases
            AnalyzeTopicKeywords(tokens, topicSignals);
        }

        // Determine most likely intent
        var topIntent = intentSignals.ContainsKey("expense_inquiry")
            ? new KeyValuePair<string, int>("expense_inquiry", intentSignals["expense_inquiry"])
            : intentSignals.OrderByDescending(x => x.Value).FirstOrDefault();
        var topTopic = topicSignals.OrderByDescending(x => x.Value).FirstOrDefault();

        var confidence = CalculateIntentConfidence(intentSignals, topicSignals, recentMessages.Count);

        _logger.LogDebug(
            "Intent inference for session {SessionId}: intent={Intent} (score={IntentScore}), topic={Topic} (score={TopicScore}), confidence={Confidence}",
            sessionId, topIntent.Key, topIntent.Value, topTopic.Key, topTopic.Value, confidence);

        return new IntentInferenceResult
        {
            Success = confidence > 0.3f,
            InferredIntent = topIntent.Key ?? topTopic.Key,
            InferredTopic = topTopic.Key,
            Confidence = confidence,
            Reasoning = confidence > 0.5f
                ? $"Based on {recentMessages.Count} recent messages with strong keyword signals."
                : $"Based on {recentMessages.Count} recent messages with weak signals."
        };
    }

    /// <summary>
    /// Checks if the current query can be resolved with partial context.
    /// </summary>
    public bool CanResolveWithPartialContext(string query, ConversationContext? context)
    {
        if (context == null || context.Entities.Count == 0)
            return false;

        var normalized = IntentTextNormalizer.Normalize(query);

        // Check for explicit entity references that need resolution
        var explicitReferences = new[]
        {
            "phong nao", "ben nao", "ai do", "gi do", "doi tuong nao",
            "thu gi", "o dau", "cua ai", "cua gi"
        };

        foreach (var reference in explicitReferences)
        {
            if (normalized.Contains(reference, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Check if query contains enough specific information
        return HasSufficientSpecificity(normalized);
    }

    /// <summary>
    /// TODO: [DRY VIOLATION] Entity extraction methods below duplicate logic in ContextResolver.
    /// See ContextResolver.ExtractEntitiesFromQuery (lines 408-519) which extracts the same
    /// entity types (DEPARTMENT, PERSON, MONEY, DATE) using similar patterns.
    /// Consider extracting to a shared IEntityExtractor service.
    /// </summary>
    private HistoryExtractedContext ExtractContextFromHistory(Guid sessionId, List<ChatMessage> recentMessages)
    {
        var entities = new List<TrackedEntity>();
        var intentSignals = new Dictionary<string, int>();

        for (int i = 0; i < recentMessages.Count; i++)
        {
            var message = recentMessages[i];
            var normalized = _textNormalizer.Normalize(message.Content);
            var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Extract department entities
            ExtractDepartmentsFromTokens(tokens, entities, i + 1);

            // Extract money entities
            ExtractMoneyFromTokens(tokens, entities, i + 1);

            // Extract date entities
            ExtractDatesFromTokens(tokens, entities, i + 1);

            // Extract action/intent keywords
            AnalyzeIntentKeywords(normalized, intentSignals);
        }

        var inferredIntent = intentSignals.Count > 0
            ? intentSignals.OrderByDescending(x => x.Value).First().Key
            : null;

        return new HistoryExtractedContext
        {
            Entities = entities,
            InferredIntent = inferredIntent
        };
    }

    private void ExtractDepartmentsFromTokens(string[] tokens, List<TrackedEntity> entities, int turnNumber)
    {
        var departmentKeywords = new[] { "phong", "ban", "bo phan", "to", "team" };

        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i].ToLowerInvariant();

            if (departmentKeywords.Any(k => token.Contains(k)))
            {
                // Collect department name
                var nameTokens = new List<string> { tokens[i] };
                for (int j = i + 1; j < Math.Min(i + 4, tokens.Length); j++)
                {
                    if (IsStopWord(tokens[j]))
                        break;
                    nameTokens.Add(tokens[j]);
                }

                if (nameTokens.Count > 1)
                {
                    var name = string.Join(" ", nameTokens);
                    if (!entities.Any(e => e.CanonicalName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        entities.Add(TrackedEntity.Create(name, EntityType.DEPARTMENT, turnNumber));
                    }
                }
            }
        }
    }

    private void ExtractMoneyFromTokens(string[] tokens, List<TrackedEntity> entities, int turnNumber)
    {
        var moneyKeywords = new[] { "dong", "k", "nghin", "trieu", "ty", "vnd", "usd", "dollar" };
        var numberTokens = new[] { "mot", "hai", "ba", "bon", "nam", "sau", "bay", "tam", "chin", "muoi" };

        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i].ToLowerInvariant();

            if (moneyKeywords.Any(m => token.Contains(m)))
            {
                var amountTokens = new List<string>();

                // Look back for numbers
                for (int j = Math.Max(0, i - 3); j < i; j++)
                {
                    if (IsStopWord(tokens[j]))
                        continue;
                    if (double.TryParse(tokens[j], out _) || numberTokens.Contains(tokens[j].ToLowerInvariant()))
                    {
                        amountTokens.Insert(0, tokens[j]);
                    }
                }

                amountTokens.Add(tokens[i]);
                var amountPhrase = string.Join(" ", amountTokens);

                if (!entities.Any(e => e.CanonicalName.Contains(amountPhrase, StringComparison.OrdinalIgnoreCase)))
                {
                    entities.Add(TrackedEntity.Create(amountPhrase, EntityType.MONEY, turnNumber));
                }
            }
        }
    }

    private void ExtractDatesFromTokens(string[] tokens, List<TrackedEntity> entities, int turnNumber)
    {
        var dateKeywords = new[] { "ngay", "thang", "nam", "hom nay", "hom qua", "ngay mai", "tuan", "quy" };

        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i].ToLowerInvariant();

            if (dateKeywords.Any(d => token.Contains(d)))
            {
                var dateTokens = new List<string> { tokens[i] };

                // Look ahead for more date-related tokens
                for (int j = i + 1; j < Math.Min(i + 3, tokens.Length); j++)
                {
                    if (IsStopWord(tokens[j]) || double.TryParse(tokens[j], out _))
                    {
                        dateTokens.Add(tokens[j]);
                    }
                    else
                    {
                        break;
                    }
                }

                var datePhrase = string.Join(" ", dateTokens);
                if (!entities.Any(e => e.CanonicalName.Contains(datePhrase, StringComparison.OrdinalIgnoreCase)))
                {
                    entities.Add(TrackedEntity.Create(datePhrase, EntityType.DATE, turnNumber));
                }
            }
        }
    }

    private void AnalyzeIntentKeywords(string normalizedText, Dictionary<string, int> signals)
    {
        var intentPatterns = new Dictionary<string, string[]>
        {
            ["expense_inquiry"] = new[] { "chi phi", "tien", "thu chi", "tai chinh", "ngan sach", "bao cao tai chinh", "thanh toan", "hoa don" },
            ["department_inquiry"] = new[] { "phong ban", "bo phan", "nhan su", "ke toan", "kinh doanh", "ky thuat", "marketing" },
            ["comparison"] = new[] { "so sanh", "hon", "kem", "bang", "khac nhau", "giong nhau", "nao tot hon" },
            ["status_inquiry"] = new[] { "trang thai", "tinh trang", "dang", "hoan thanh", "chua", "o dau", "ra sao" },
            ["budget_inquiry"] = new[] { "ngan sach", "quy", "duoc duyet", "gioi han", "toi da", "toi thieu" },
            ["approval_request"] = new[] { "duyet", "phe duyet", "xac nhan", "dong y", "ok" }
        };

        foreach (var (intent, keywords) in intentPatterns)
        {
            foreach (var keyword in keywords)
            {
                if (normalizedText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    signals.TryGetValue(intent, out var count);
                    signals[intent] = count + 1;
                }
            }
        }
    }

    private void AnalyzeTopicKeywords(string[] tokens, Dictionary<string, int> signals)
    {
        var topicPatterns = new Dictionary<string, string[]>
        {
            ["chi_phi"] = new[] { "chi phi", "tien", "dong", "vnd", "ngan sach" },
            ["phong_ban"] = new[] { "phong", "ban", "bo phan", "team", "to" },
            ["nhan_su"] = new[] { "nhan vien", "nhan su", "luong", "thuong", "phuc loi" },
            ["bao_cao"] = new[] { "bao cao", "thong ke", "so lieu", "bieu do", "xem" },
            ["hop_dong"] = new[] { "hop dong", "ky", "thoa thuan", "dieu khoan" },
            ["du_an"] = new[] { "du an", "project", "cong viec", "task", "milestone" }
        };

        foreach (var (topic, keywords) in topicPatterns)
        {
            foreach (var keyword in keywords)
            {
                foreach (var token in tokens)
                {
                    if (token.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        signals.TryGetValue(topic, out var count);
                        signals[topic] = count + 1;
                    }
                }
            }
        }
    }

    private float CalculateIntentConfidence(Dictionary<string, int> intentSignals, Dictionary<string, int> topicSignals, int messageCount)
    {
        if (intentSignals.Count == 0 && topicSignals.Count == 0)
            return 0f;

        var maxIntentSignal = intentSignals.Values.DefaultIfEmpty(0).Max();
        var maxTopicSignal = topicSignals.Values.DefaultIfEmpty(0).Max();
        var totalSignals = intentSignals.Values.Sum() + topicSignals.Values.Sum();

        // More messages and stronger signals = higher confidence
        var signalStrength = Math.Min(totalSignals / 10f, 1f);
        var messageCountFactor = Math.Min(messageCount / 5f, 1f);

        return Math.Min((signalStrength * 0.6f) + (messageCountFactor * 0.4f), 1f);
    }

    private bool HasSufficientSpecificity(string normalizedQuery)
    {
        var tokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Short queries without special markers are typically follow-ups
        if (tokens.Length <= 3)
            return false;

        // Check for entity-specific markers
        var specificMarkers = new[]
        {
            "phong", "ban", "bo phan", "cua", "ngay", "thang",
            "tien", "dong", "trieu", "nhan vien", "hop dong"
        };

        return tokens.Any(t => specificMarkers.Any(m => t.Contains(m, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsStopWord(string token)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "và", "thì", "có", "không", "theo", "với", "cho", "để", "của", "trong",
            "là", "một", "các", "được", "này", "kia", "đó", "bên", "ở"
        };
        return stopWords.Contains(token);
    }

    private string GenerateLowConfidencePrompt(string query, IReadOnlyList<ChatMessage>? history)
    {
        var lastEntity = history?.LastOrDefault(m => m.Role == ChatMessageRole.User)?.Content;

        if (!string.IsNullOrEmpty(lastEntity) && history?.Count > 0)
        {
            return $"Tôi thấy bạn đang hỏi về \"{query}\", nhưng tôi chưa rõ đối tượng cụ thể. Bạn có thể cho biết thêm chi tiết được không? Ví dụ: phòng ban, thời gian, hoặc số tiền cụ thể.";
        }

        return "Tôi không rõ ý của bạn. Bạn có thể cho biết thêm chi tiết về đối tượng bạn đang hỏi không?";
    }

    private string GenerateUnknownEntityPrompt(string query, IReadOnlyList<ChatMessage>? history)
    {
        if (history != null && history.Count > 0)
        {
            var mentionedEntities = ExtractRecentlyMentionedEntities(history);
            if (mentionedEntities.Count > 0)
            {
                var entityList = string.Join(", ", mentionedEntities);
                return $"Tôi thấy bạn đề cập đến đối tượng trong câu hỏi, nhưng tôi chưa xác định được đó là gì. Bạn đang nói về {entityList} phải không?";
            }
        }

        return "Tôi chưa hiểu đối tượng bạn đang hỏi là gì. Bạn có thể nói rõ hơn không? Ví dụ: \"Chi phí phòng nhân sự\" hoặc \"Báo cáo tháng này\".";
    }

    private string GenerateUnclearIntentPrompt(string query, IReadOnlyList<ChatMessage>? history)
    {
        var lastTopic = history?.LastOrDefault(m => m.Role == ChatMessageRole.User)?.Content;

        if (!string.IsNullOrEmpty(lastTopic) && lastTopic.Length > 10)
        {
            return $"Dựa trên câu hỏi trước đó, có phải bạn muốn hỏi về \"{lastTopic}\" không? Nếu đúng, tôi có thể giúp bạn. Nếu không, xin cho tôi biết thêm chi tiết.";
        }

        return "Xin lỗi, tôi chưa hiểu bạn muốn hỏi về điều gì. Bạn có thể cho tôi biết thêm thông tin được không?";
    }

    private string GenerateMultipleMatchesPrompt(string query, IReadOnlyList<ChatMessage>? history)
    {
        if (history != null && history.Count > 0)
        {
            var mentionedEntities = ExtractRecentlyMentionedEntities(history);
            if (mentionedEntities.Count > 1)
            {
                var entityList = string.Join(", ", mentionedEntities.Take(3));
                return $"Tôi thấy có nhiều đối tượng được nhắc đến: {entityList}. Bạn đang hỏi về đối tượng nào cụ thể?";
            }
        }

        return "Có vẻ như có nhiều đối tượng phù hợp với câu hỏi của bạn. Bạn có thể cho biết cụ thể hơn đối tượng nào bạn quan tâm không?";
    }

    private string GenerateAmbiguousReferencePrompt(string query, IReadOnlyList<ChatMessage>? history)
    {
        if (history != null && history.Count > 0)
        {
            var recentContext = history.TakeLast(3).Select(m => m.Content).ToList();
            if (recentContext.Count > 0)
            {
                return $"Tôi thấy bạn đề cập đến \"{query}\" nhưng chưa rõ đối tượng cụ thể. Bạn có thể nói rõ hơn là bạn đang hỏi về gì không?";
            }
        }

        return "Tôi chưa rõ đối tượng bạn đang đề cập. Bạn có thể cho tôi biết cụ thể hơn không?";
    }

    private string GenerateGenericClarificationPrompt(string query)
    {
        return "Xin lỗi, tôi chưa hiểu rõ ý bạn. Bạn có thể cho tôi biết thêm chi tiết để tôi có thể hỗ trợ bạn tốt hơn không?";
    }

    private List<string> ExtractRecentlyMentionedEntities(IReadOnlyList<ChatMessage> history)
    {
        var entities = new List<string>();
        var recentMessages = history.TakeLast(5);

        foreach (var message in recentMessages)
        {
            var normalized = _textNormalizer.Normalize(message.Content);
            var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                if (!IsStopWord(token) && token.Length > 2 && !entities.Contains(token))
                {
                    entities.Add(token);
                }
            }
        }

        return entities.Take(5).ToList();
    }
}

/// <summary>
/// Internal class to hold extracted context from history.
/// </summary>
internal class HistoryExtractedContext
{
    public List<TrackedEntity> Entities { get; init; } = [];
    public string? InferredIntent { get; init; }
}

internal class ExtractedContext
{
    public List<TrackedEntity> Entities { get; init; } = [];
    public string? InferredIntent { get; init; }
}
