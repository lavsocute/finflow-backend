using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Summarizes long conversation histories to reduce token usage in RAG prompts.
/// </summary>
public sealed class ContextSummarizationService : IContextSummarizationService
{
    private readonly ILogger<ContextSummarizationService> _logger;
    private readonly HttpClient _httpClient;
    private readonly GroqChatOptions _options;

    private const int MaxHistoryMessages = 15;
    private const int MaxHistoryCharacters = 4000;
    private const int MaxSummaryLength = 500;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ContextSummarizationService(
        ILogger<ContextSummarizationService> logger,
        HttpClient httpClient,
        IOptions<GroqChatOptions> options)
    {
        _logger = logger;
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string> SummarizeAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default)
    {
        if (history.Count == 0)
            return string.Empty;

        _logger.LogInformation("Summarizing {MessageCount} messages", history.Count);

        // Build summary prompt
        var conversationText = string.Join("\n",
            history.Select(m => $"{m.Role}: {Truncate(m.Content, 200)}"));

        var summaryPrompt = $"""
            Tóm tắt cuộc trò chuyện sau thành 2-3 câu tiếng Việt.
            Giữ lại:
            - Chủ đề chính
            - Các thực thể quan trọng (tên phòng ban, người, số tiền)
            - Câu hỏi của người dùng
            - Kết quả đã trả lời

            Cuộc trò chuyện:
            {conversationText}

            Tóm tắt (tiếng Việt, ngắn gọn):
            """;

        // In production, this would call the LLM
        // For now, return a placeholder
        var summary = await GenerateSummaryAsync(summaryPrompt, ct);

        return summary;
    }

    public Task<bool> ShouldSummarizeAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default)
    {
        if (history.Count < MaxHistoryMessages)
            return Task.FromResult(false);

        var totalChars = history.Sum(m => m.Content.Length);
        if (history.Count == MaxHistoryMessages && totalChars <= MaxHistoryCharacters)
            return Task.FromResult(false);

        _logger.LogInformation(
            "ShouldSummarize: {MessageCount} messages, {CharCount} chars",
            history.Count, totalChars);

        return Task.FromResult(true);
    }

    private async Task<string> GenerateSummaryAsync(string prompt, CancellationToken ct)
    {
        var messages = new List<object>
        {
            new { role = "system", content = "Bạn là trợ lý tóm tắt cuộc trò chuyện. Chỉ trả lời bằng 2-3 câu tiếng Việt, không thêm lời mở đầu." },
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
                _logger.LogWarning("LLM summarization failed with status {Status}: {Response}", response.StatusCode, responseText);
                return $"Tóm tắt cuộc trò chuyện gần đây.";
            }

            var json = JsonSerializer.Deserialize<OpenRouterChatResponse>(responseText, SerializerOptions);
            var summary = json?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? string.Empty;

            if (summary.Length > MaxSummaryLength)
                summary = summary[..(MaxSummaryLength - 3)] + "...";

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate summary via LLM");
            return $"Tóm tắt cuộc trò chuyện gần đây.";
        }
    }

    private static Uri BuildChatCompletionsUri(string baseUrl)
    {
        return ChatCompletionsEndpointBuilder.Build(baseUrl);
    }

    private record OpenRouterChatResponse(
        List<OpenRouterChoice> Choices
    );

    private record OpenRouterChoice(
        OpenRouterMessage Message
    );

    private record OpenRouterMessage(
        string Content
    );

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return text[..(maxLength - 3)] + "...";
    }
}
