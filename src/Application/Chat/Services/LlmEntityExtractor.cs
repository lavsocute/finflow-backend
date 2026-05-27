using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// LLM-powered entity extractor using GPT-4o-mini or similar lightweight model
/// with function calling / tool calling for structured entity extraction.
/// </summary>
public sealed class LlmEntityExtractor : IEntityExtractor, ILlmEntityExtractor
{
    private readonly HttpClient _httpClient;
    private readonly LlmEntityExtractorOptions _options;
    private readonly ILogger<LlmEntityExtractor> _logger;
    private readonly ITextNormalizer _textNormalizer;
    private readonly Uri _completionsUri;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private const int TimeoutSeconds = 10;
    private const double DefaultConfidence = 0.85;

    public LlmEntityExtractor(
        HttpClient httpClient,
        IOptions<LlmEntityExtractorOptions> options,
        ILogger<LlmEntityExtractor> logger,
        ITextNormalizer? textNormalizer = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _textNormalizer = textNormalizer ?? new TextNormalizer();

        _completionsUri = ChatCompletionsEndpointBuilder.Build(_options.BaseUrl);
    }

    public async Task<IReadOnlyList<ExtractedEntity>> ExtractAsync(
        string message,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            return [];

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        try
        {
            var conversationHistory = BuildConversationHistory(history);
            var requestBody = BuildRequest(message, conversationHistory);

            var json = JsonSerializer.Serialize(requestBody, SerializerOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_completionsUri, content, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Entity extraction returned {Status}; falling back to empty result.", response.StatusCode);
                return [];
            }

            var responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            return ParseEntities(responseText);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Entity extraction timed out after {Timeout}s", TimeoutSeconds);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Entity extraction failed");
            return [];
        }
    }

    private string BuildConversationHistory(IReadOnlyList<ChatMessage> history)
    {
        if (history.Count == 0)
            return string.Empty;

        return string.Join("\n",
            history.Select(m => $"{m.Role}: {Truncate(m.Content, 300)}"));
    }

    private string BuildHistoryEntities(IReadOnlyList<ChatMessage> history)
    {
        if (history.Count == 0) return "No history available.";
        var recent = history.TakeLast(5);
        return string.Join("\n", recent.Select(m => $"{m.Role}: {Truncate(m.Content, 200)}"));
    }

    private object BuildRequest(string message, string conversationHistory)
    {
        var systemPrompt = """
            Bạn là trợ lý trích xuất thực thể cho hệ thống tài chính FinFlow.
            Trích xuất tất cả các thực thể từ tin nhắn của người dùng và ngữ cảnh hội thoại.

            QUAN TRỌNG - Xử lý tham chiếu theo ngữ cảnh:
            Khi tin nhắn chứa các từ tham chiếu như "tháng đó", "ngày đó", "hôm đó", "tuần đó", "nó", "họ", "bên đó":
            1. Xem xét LỊCH SỬ HỘI THOẠI để xác định thực thể thực sự được tham chiếu
            2. "tháng đó" cần được giải quyết thành tháng cụ thể (ví dụ: "tháng 5") dựa trên ngữ cảnh trước đó
            3. Nếu có tháng/năm/ngày được đề cập trong lịch sử hội thoại, sử dụng chúng để xác định "tháng đó", "ngày đó"
            4. Không chỉ trích xuất "tháng đó" như một cụm từ - cần xác định THÁNG NÀO được tham chiếu

            Các loại thực thể cần trích xuất:
            - PERSON: Tên người (nhân viên, khách hàng, v.v.)
            - ORGANIZATION: Tên tổ chức, công ty
            - MONEY: Số tiền, giá trị tài chính (vnđ, USD, v.v.)
            - DATE: Ngày tháng, thời gian (dd/mm/yyyy, tháng, quý, năm) - LUÔN cố gắng xác định ngày/tháng cụ thể từ ngữ cảnh
            - LOCATION: Địa điểm, địa chỉ
            - DOCUMENT: Chứng từ, hóa đơn, tài liệu
            - CONCEPT: Khái niệm, thuật ngữ tài chính (ngân sách, chi phí, doanh thu)
            - ACTION: Hành động (thêm, sửa, xóa, xem, tạo, duyệt, v.v.)
            - DEPARTMENT: Phòng ban (Kế toán, Nhân sự, Kỹ thuật, v.v.)
            - VENDOR: Nhà cung cấp, đối tác
            - EXPENSE: Loại chi phí, danh mục chi tiêu
            - BUDGET: Ngân sách, hạn mức

            Trả lời CHỈ bằng JSON array với format được mô tả trong function call.
            Nếu không có thực thể nào, trả về empty array.
            """;

        return new
        {
            model = _options.EffectiveModel,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = BuildUserMessage(message, conversationHistory) }
            },
            tools = new[]
            {
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "extract_entities",
                        description = "Trích xuất các thực thể từ tin nhắn người dùng và ngữ cảnh hội thoại",
                        parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                entities = new
                                {
                                    type = "array",
                                    description = "Danh sách các thực thể được trích xuất",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            text = new
                                            {
                                                type = "string",
                                                description = "Text gốc của thực thể"
                                            },
                                            type = new
                                            {
                                                type = "string",
                                                @enum = new[] { "PERSON", "ORGANIZATION", "MONEY", "DATE", "LOCATION", "DOCUMENT", "CONCEPT", "ACTION", "DEPARTMENT", "VENDOR", "EXPENSE", "BUDGET", "UNKNOWN" },
                                                description = "Loại thực thể"
                                            },
                                            confidence = new
                                            {
                                                type = "number",
                                                description = "Độ tin cậy (0.0 - 1.0)"
                                            },
                                            attributes = new
                                            {
                                                type = "object",
                                                description = "Các thuộc tính bổ sung",
                                                additionalProperties = true
                                            }
                                        },
                                        required = new[] { "text", "type", "confidence" }
                                    }
                                }
                            },
                            required = new[] { "entities" }
                        }
                    }
                }
            },
            tool_choice = new { type = "function", function = new { name = "extract_entities" } },
            temperature = 0.1,
            max_tokens = 500
        };
    }

    private static string BuildUserMessage(string message, string conversationHistory)
    {
        var sb = new StringBuilder();
        sb.Append("Tin nhắn hiện tại: ");
        sb.Append(message);

        if (!string.IsNullOrWhiteSpace(conversationHistory))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append("Ngữ cảnh hội thoại trước đó:\n");
            sb.Append(conversationHistory);
        }

        sb.AppendLine();
        sb.AppendLine();
        sb.Append("Trích xuất tất cả thực thể từ tin nhắn trên.");

        return sb.ToString();
    }

    private IReadOnlyList<ExtractedEntity> ParseEntities(string responseText)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseText);

            if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0)
            {
                _logger.LogDebug("No choices in entity extraction response");
                return [];
            }

            var firstChoice = choices[0];

            // Handle tool_calls response
            if (firstChoice.TryGetProperty("message", out var message) &&
                message.TryGetProperty("tool_calls", out var toolCalls) &&
                toolCalls.GetArrayLength() > 0)
            {
                var toolCall = toolCalls[0];
                if (toolCall.TryGetProperty("function", out var function) &&
                    function.TryGetProperty("arguments", out var arguments))
                {
                    var argsJson = arguments.GetString();
                    if (!string.IsNullOrWhiteSpace(argsJson))
                    {
                        return DeserializeEntities(argsJson);
                    }
                }
            }

            // Handle direct content response (fallback)
            if (message.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                var contentStr = content.GetString();
                if (!string.IsNullOrWhiteSpace(contentStr))
                {
                    return DeserializeEntities(contentStr);
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse entity extraction response: {Response}", Truncate(responseText, 200));
        }

        return [];
    }

    private IReadOnlyList<ExtractedEntity> DeserializeEntities(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("entities", out var entities))
                return [];

            var results = new List<ExtractedEntity>();

            foreach (var entity in entities.EnumerateArray())
            {
                if (!entity.TryGetProperty("text", out var textElement) ||
                    textElement.ValueKind != JsonValueKind.String)
                    continue;

                var text = textElement.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var type = EntityType.UNKNOWN;
                if (entity.TryGetProperty("type", out var typeElement) &&
                    typeElement.ValueKind == JsonValueKind.String)
                {
                    var typeStr = typeElement.GetString() ?? string.Empty;
                    if (Enum.TryParse<Domain.Chat.EntityType>(typeStr, ignoreCase: true, out var parsedType))
                    {
                        type = parsedType;
                    }
                }

                var confidence = DefaultConfidence;
                if (entity.TryGetProperty("confidence", out var confElement) &&
                    confElement.ValueKind == JsonValueKind.Number)
                {
                    confidence = confElement.GetDouble();
                }

                
                results.Add(new ExtractedEntity
                {
                    Text = text,
                    Type = type,
                    Confidence = (float)confidence,
                    NormalizedForm = null
                });
            }

            return results;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize entities from: {Json}", Truncate(json, 200));
            return [];
        }
    }

    private static object GetJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => element.GetRawText()
        };
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text[..(maxLength - 3)] + "...";
    }

    // ILlmEntityExtractor implementation - delegates to ExtractAsync

    Task<FollowUpDetectionResult> ILlmEntityExtractor.DetectFollowUpAsync(
        string query,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct)
    {
        // Delegate to main extraction for entity analysis, then determine follow-up
        return DetectFollowUpAsync(query, history, ct);
    }

    private async Task<FollowUpDetectionResult> DetectFollowUpAsync(
        string query,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || history.Count == 0)
            return new FollowUpDetectionResult { IsFollowUp = false, Confidence = 0f };

        if (!_options.Enabled)
            return DetectFollowUpFallback(query, history);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        try
        {
            var historyContext = BuildConversationHistory(history);
            var systemPrompt = @"You are a conversation analyst. Determine if the user's message is a follow-up question that references previous conversation context.

Follow-up indicators:
- Uses pronouns like ""nó"", ""đó"", ""bên đó"", ""phòng đó""
- Asks about something mentioned before (""còn gì"", ""thế còn"")
- Continues a comparison (""so sánh với"")
- References implicit context without naming entities explicitly

Respond with JSON only:
{""isFollowUp"": true/false, ""confidence"": 0.0-1.0, ""followUpType"": ""implicit""|""comparison""|""continuation""|null, ""reasoning"": ""brief explanation""}";

            var requestBody = new
            {
                model = _options.EffectiveModel,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = $"History:\n{historyContext}\n\nCurrent query: {query}" }
                },
                temperature = 0.1,
                max_tokens = 150
            };

            var json = JsonSerializer.Serialize(requestBody, SerializerOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_completionsUri, content, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Follow-up detection returned {Status}; using fallback", response.StatusCode);
                return DetectFollowUpFallback(query, history);
            }

            var responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            return ParseFollowUpResponse(responseText);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Follow-up detection timed out; using fallback");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Follow-up detection failed; using fallback");
        }

        return DetectFollowUpFallback(query, history);
    }

    async Task<IReadOnlyList<ExtractedEntity>> ILlmEntityExtractor.ExtractEntitiesAsync(
        string query,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct)
    {
        return await ExtractAsync(query, history, ct).ConfigureAwait(false);
    }

    Task<IReadOnlyList<ExtractedEntity>> ILlmEntityExtractor.ExtractEntitiesAsync(
        string query,
        CancellationToken ct)
    {
        return ExtractAsync(query, [], ct);
    }

    Task<IReadOnlyList<EntityResolution>> ILlmEntityExtractor.ResolveEntityReferencesAsync(
        string query,
        ConversationContext context,
        IReadOnlyList<ExtractedEntity> entities,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct)
    {
        return ResolveEntityReferencesAsync(query, context, entities, history, ct);
    }

    Task<IReadOnlyList<EntityResolution>> ILlmEntityExtractor.ResolveEntityReferencesAsync(
        string query,
        ConversationContext context,
        IReadOnlyList<ExtractedEntity> entities,
        CancellationToken ct)
    {
        return ResolveEntityReferencesAsync(query, context, entities, [], ct);
    }

    private async Task<IReadOnlyList<EntityResolution>> ResolveEntityReferencesAsync(
        string query,
        ConversationContext context,
        IReadOnlyList<ExtractedEntity> entities,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || context == null || entities.Count == 0)
            return [];

        if (!_options.Enabled)
            return ResolveEntitiesFallback(query, context, entities);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        try
        {
            var contextEntities = BuildContextEntities(context);
            var historyContext = BuildHistoryEntities(history);
            var systemPrompt = @"You are a context resolution assistant. Given the query, previously mentioned entities, and conversation history, resolve references in the query to actual entities.

CRITICAL: When resolving references like 'tháng đó', 'ngày đó', 'hôm đó', 'tuần đó', 'nó', 'họ', 'bên đó':
1. Use the CONVERSATION HISTORY to determine what actual month/date/entity is being referenced
2. 'tháng đó' should be resolved to the specific month mentioned in PREVIOUS Q&A (e.g., 'tháng 5')
3. Do NOT leave references unresolved - always try to map them to specific entities from history
4. If the history mentions 'tháng 5 năm 2024' and user says 'tháng đó', resolve to 'tháng 5 năm 2024'

Respond with JSON array only:
[{""original"": ""referenced text"", ""resolved"": ""actual entity name"", ""confidence"": 0.0-1.0}]";

            var requestBody = new
            {
                model = _options.EffectiveModel,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = $"Known entities:\n{contextEntities}\n\nConversation history:\n{historyContext}\n\nQuery: {query}" }
                },
                temperature = 0.1,
                max_tokens = 300
            };

            var json = JsonSerializer.Serialize(requestBody, SerializerOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_completionsUri, content, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Entity resolution returned {Status}; using fallback", response.StatusCode);
                return ResolveEntitiesFallback(query, context, entities);
            }

            var responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            return ParseResolutionResponse(responseText);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Entity resolution timed out; using fallback");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Entity resolution failed; using fallback");
        }

        return ResolveEntitiesFallback(query, context, entities);
    }

    private string BuildContextEntities(ConversationContext context)
    {
        if (context.Entities.Count == 0)
            return "No entities in context.";

        return string.Join("\n", context.Entities.Select(e =>
            $"- {e.CanonicalName} ({e.Type}), aliases: {string.Join(", ", e.Aliases)}"));
    }

    private FollowUpDetectionResult DetectFollowUpFallback(string query, IReadOnlyList<ChatMessage> history)
    {
        if (history.Count < 1)
            return new FollowUpDetectionResult { IsFollowUp = false, Confidence = 0f };

        var normalizedQuery = _textNormalizer.Normalize(query);
        var followUpMarkers = new[] { "còn", "thế", "vậy", "đó", "bên", "so sánh", "với" };
        var hasFollowUpMarker = followUpMarkers.Any(m => normalizedQuery.Contains(m));

        var pronounReferences = new[] { "nó", "đó", "bên đó", "phòng đó", "ai đó", "gì đó" };
        var hasPronounRef = pronounReferences.Any(p => normalizedQuery.Contains(p));

        var hasShortQuery = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3;
        var lastMessage = history.LastOrDefault(m => m.Role == ChatMessageRole.User);
        var hasLongPrevious = lastMessage != null &&
            _textNormalizer.Normalize(lastMessage.Content).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 5;

        var confidence = hasFollowUpMarker ? 0.5f : 0f;
        if (hasPronounRef) confidence += 0.2f;
        if (hasShortQuery && hasLongPrevious) confidence += 0.2f;

        return new FollowUpDetectionResult
        {
            IsFollowUp = confidence >= 0.25f,
            Confidence = Math.Min(confidence, 1.0f),
            FollowUpType = hasFollowUpMarker ? "implicit" : null,
            Reasoning = "Fallback heuristic detection"
        };
    }

    private IReadOnlyList<EntityResolution> ResolveEntitiesFallback(
        string query,
        ConversationContext context,
        IReadOnlyList<ExtractedEntity> entities)
    {
        var resolutions = new List<EntityResolution>();

        foreach (var entity in entities)
        {
            var matched = context.FindEntity(entity.Text);
            if (matched != null)
            {
                matched.AddAlias(entity.Text);
                resolutions.Add(new EntityResolution
                {
                    Original = entity.Text,
                    Resolved = matched.CanonicalName,
                    Source = "context",
                    Confidence = 0.8f * (float)entity.Confidence
                });
            }
            else
            {
                var byType = context.FindEntityByType(entity.Type);
                if (byType != null)
                {
                    byType.AddAlias(entity.Text);
                    resolutions.Add(new EntityResolution
                    {
                        Original = entity.Text,
                        Resolved = byType.CanonicalName,
                        Source = $"context_{entity.Type.ToString().ToLowerInvariant()}",
                        Confidence = 0.7f * (float)entity.Confidence
                    });
                }
            }
        }

        return resolutions;
    }

    private FollowUpDetectionResult ParseFollowUpResponse(string responseText)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            var isFollowUp = root.TryGetProperty("isFollowUp", out var isFuProp) && isFuProp.GetBoolean();
            var confidence = root.TryGetProperty("confidence", out var confProp) ? (float)confProp.GetDouble() : 0.5f;
            var followUpType = root.TryGetProperty("followUpType", out var typeProp) && typeProp.ValueKind == JsonValueKind.String
                ? typeProp.GetString()
                : null;
            var reasoning = root.TryGetProperty("reasoning", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String
                ? reasonProp.GetString()
                : null;

            return new FollowUpDetectionResult
            {
                IsFollowUp = isFollowUp,
                Confidence = confidence,
                FollowUpType = followUpType,
                Reasoning = reasoning
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse follow-up detection response: {Response}", Truncate(responseText, 200));
            return new FollowUpDetectionResult { IsFollowUp = false, Confidence = 0f };
        }
    }

    private IReadOnlyList<EntityResolution> ParseResolutionResponse(string responseText)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
                return [];

            var resolutions = new List<EntityResolution>();
            foreach (var item in root.EnumerateArray())
            {
                var original = item.TryGetProperty("original", out var orig) && orig.ValueKind == JsonValueKind.String ? orig.GetString() : null;
                var resolved = item.TryGetProperty("resolved", out var res) && res.ValueKind == JsonValueKind.String ? res.GetString() : null;
                var confidence = item.TryGetProperty("confidence", out var conf) ? (float)conf.GetDouble() : 0.5f;

                if (!string.IsNullOrEmpty(original) && !string.IsNullOrEmpty(resolved))
                {
                    resolutions.Add(new EntityResolution
                    {
                        Original = original,
                        Resolved = resolved,
                        Source = "llm",
                        Confidence = confidence
                    });
                }
            }
            return resolutions;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse entity resolution response: {Response}", Truncate(responseText, 200));
            return [];
        }
    }
}

public sealed class LlmEntityExtractorOptions
{
    public bool Enabled { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    public string Model { get; set; } = string.Empty;
    public string ChatModel { get; set; } = "llama-3.3-70b-versatile";

    public string EffectiveModel =>
        !string.IsNullOrWhiteSpace(Model)
            ? Model
            : ChatModel;
}
