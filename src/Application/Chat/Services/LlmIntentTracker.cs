using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Uses LLM to analyze conversation intent, detecting pivots, clarifications, and continuations.
/// </summary>
public sealed class LlmIntentTracker : ILlmIntentTracker
{
    private readonly ILogger<LlmIntentTracker> _logger;
    private readonly HttpClient _httpClient;
    private readonly GroqChatOptions _options;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public LlmIntentTracker(
        ILogger<LlmIntentTracker> logger,
        HttpClient httpClient,
        IOptions<GroqChatOptions> options)
    {
        _logger = logger;
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<LlmIntentAnalysis> AnalyzeIntentAsync(
        string currentMessage,
        IReadOnlyList<ChatMessage> conversationHistory,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Analyzing intent for message: {Message}", Truncate(currentMessage, 100));

        var prompt = BuildIntentAnalysisPrompt(currentMessage, conversationHistory);
        var result = await CallLlmAsync(prompt, ct);

        return result;
    }

    private string BuildIntentAnalysisPrompt(string currentMessage, IReadOnlyList<ChatMessage> history)
    {
        var historyText = BuildHistoryText(history);

        return $"""
            Bạn là chuyên gia phân tích intent trong cuộc trò chuyện tài chính doanh nghiệp.

            Phân tích tin nhắn của người dùng và trả lời JSON với các trường:
            - intentType: Loại intent chính (query_expense, query_budget, query_report, create_expense, approve_expense, cancel_expense, update_expense, query_department, query_vendor, query_trend, greeting, clarification, pivot, continuation, unknown)
            - confidence: Mức độ tin cậy (high, medium, low)
            - isPivot: true nếu người dùng đang chuyển sang chủ đề mới hoàn toàn
            - isClarification: true nếu người dùng đang hỏi làm rõ / follow-up
            - isContinuation: true nếu người dùng đang tiếp tục intent trước đó
            - reasoning: Giải thích ngắn tại sao bạn chọn intent này (tiếng Việt)

            Lịch sử cuộc trò chuyện:
            {historyText}

            Tin nhắn hiện tại: "{currentMessage}"

            Trả lời JSON (chỉ JSON, không có text khác):
            """;
    }

    private static string BuildHistoryText(IReadOnlyList<ChatMessage> history)
    {
        if (history.Count == 0)
            return "(cuộc trò chuyện mới)";

        return string.Join("\n",
            history.TakeLast(10).Select(m => $"{m.Role}: {Truncate(m.Content, 200)}"));
    }

    private async Task<LlmIntentAnalysis> CallLlmAsync(string prompt, CancellationToken ct)
    {
        var messages = new List<object>
        {
            new { role = "system", content = "Bạn là chuyên gia phân tích intent. Chỉ trả lời JSON hợp lệ, không có text khác." },
            new { role = "user", content = prompt }
        };

        var requestBody = new
        {
            model = _options.ChatModel,
            messages
        };

        var jsonContent = JsonSerializer.Serialize(requestBody, SerializerOptions);
        var chatCompletionsUri = BuildChatCompletionsUri(_options.BaseUrl);

        using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, chatCompletionsUri) { Content = content };

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            var responseText = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("LLM intent analysis failed with status {Status}: {Response}", response.StatusCode, responseText);
                return CreateDefaultAnalysis();
            }

            var json = JsonSerializer.Deserialize<JsonElement>(responseText, SerializerOptions);
            return ParseLlmResponse(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze intent via LLM");
            return CreateDefaultAnalysis();
        }
    }

    private LlmIntentAnalysis ParseLlmResponse(JsonElement json)
    {
        try
        {
            var choices = json.GetProperty("choices");
            if (choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var message = choices[0].GetProperty("message");
                var content = message.GetProperty("content").GetString() ?? "{}";

                return ParseJsonResponse(content);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM response, using default");
        }

        return CreateDefaultAnalysis();
    }

    private LlmIntentAnalysis ParseJsonResponse(string content)
    {
        try
        {
            // Clean potential markdown code blocks
            content = content.Trim();
            if (content.StartsWith("```json"))
                content = content[7..];
            if (content.StartsWith("```"))
                content = content[3..];
            if (content.EndsWith("```"))
                content = content[..^3];
            content = content.Trim();

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var intentType = root.TryGetProperty("intentType", out var it) ? it.GetString() ?? "unknown" : "unknown";
            var confidence = root.TryGetProperty("confidence", out var conf) ? conf.GetString() ?? "low" : "low";
            var isPivot = root.TryGetProperty("isPivot", out var pivot) && pivot.GetBoolean();
            var isClarification = root.TryGetProperty("isClarification", out var clar) && clar.GetBoolean();
            var isContinuation = root.TryGetProperty("isContinuation", out var cont) && cont.GetBoolean();
            var reasoning = root.TryGetProperty("reasoning", out var reason) ? reason.GetString() ?? "" : "";

            return new LlmIntentAnalysis(
                IntentType: intentType,
                Confidence: confidence,
                IsPivot: isPivot,
                IsClarification: isClarification,
                IsContinuation: isContinuation,
                Reasoning: reasoning);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON response content: {Content}", Truncate(content, 200));
            return CreateDefaultAnalysis();
        }
    }

    private static LlmIntentAnalysis CreateDefaultAnalysis()
    {
        return new LlmIntentAnalysis(
            IntentType: "unknown",
            Confidence: "low",
            IsPivot: false,
            IsClarification: false,
            IsContinuation: false,
            Reasoning: "LLM analysis failed, defaulting to unknown");
    }

    private static Uri BuildChatCompletionsUri(string baseUrl)
    {
        return ChatCompletionsEndpointBuilder.Build(baseUrl);
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return text[..(maxLength - 3)] + "...";
    }
}
