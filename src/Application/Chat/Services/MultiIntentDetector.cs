using FinFlow.Application.Chat.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// LLM-based multi-intent detector that identifies and splits queries containing multiple distinct questions.
/// Works for any conjunction pattern (VÀ, AND, và, ;, ,, etc.) without hardcoding specific words.
/// </summary>
public sealed class MultiIntentDetector : IMultiIntentDetector
{
    private readonly ILlmChatService _llmChatService;
    private readonly GroqChatOptions _options;
    private readonly ILogger<MultiIntentDetector> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public MultiIntentDetector(
        ILlmChatService llmChatService,
        IOptions<GroqChatOptions> options,
        ILogger<MultiIntentDetector> logger)
    {
        _llmChatService = llmChatService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> DetectAndSplitAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [query];

        var detectionPrompt = BuildDetectionPrompt(query);

        try
        {
            var request = new LlmChatRequest(
                System: "You are a query analyzer for a finance chatbot. Your task is to detect if a user query contains multiple distinct questions that need separate answers.",
                Messages:
                [
                    new LlmMessage("user", detectionPrompt)
                ],
                Temperature: 0.1,
                MaxTokens: 500
            );

            var result = await _llmChatService.ChatAsync(request, ct);

            if (string.IsNullOrWhiteSpace(result.Content))
            {
                _logger.LogWarning("Multi-intent detector received empty response; treating as single intent");
                return [query];
            }

            var subQueries = ParseSubQueries(result.Content);

            if (subQueries.Count <= 1)
                return [query];

            _logger.LogInformation(
                "Multi-intent detector split query into {Count} sub-queries: {@SubQueries}",
                subQueries.Count,
                subQueries);

            return subQueries;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Multi-intent detection failed for query; falling back to single intent");
            return [query];
        }
    }

    private static string BuildDetectionPrompt(string query)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Analyze this user query and determine if it contains multiple distinct questions:");
        sb.AppendLine();
        sb.AppendLine($"Query: \"{query}\"");
        sb.AppendLine();
        sb.AppendLine("Instructions:");
        sb.AppendLine("1. Identify if the query contains 2 or more questions joined by conjunctions like 'VÀ', 'AND', 'và', ';', ',' or question patterns");
        sb.AppendLine("2. If YES: Return a JSON array of the individual questions, e.g.: [\"question 1\", \"question 2\", \"question 3\"]");
        sb.AppendLine("3. If NO: Return a JSON array with just the original query, e.g.: [\"original query\"]");
        sb.AppendLine();
        sb.AppendLine("Examples:");
        sb.AppendLine("- \"Tổng chi phí VÀ có bao nhiêu hóa đơn\" → [\"Tổng chi phí là bao nhiêu?\", \"Có bao nhiêu hóa đơn?\"]");
        sb.AppendLine("- \"tháng này tôi chi bao nhiêu\" → [\"tháng này tôi chi bao nhiêu\"]");
        sb.AppendLine("- \"cho xem hóa đơn và so sánh với tháng trước\" → [\"cho xem hóa đơn\", \"so sánh với tháng trước\"]");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: Return ONLY the JSON array, nothing else. Start with '[' and end with ']'.");

        return sb.ToString();
    }

    private static List<string> ParseSubQueries(string llmResponse)
    {
        try
        {
            // Try to extract JSON array from the response
            var jsonStart = llmResponse.IndexOf('[');
            var jsonEnd = llmResponse.LastIndexOf(']');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonArray = llmResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var parsed = JsonSerializer.Deserialize<List<string>>(jsonArray, JsonOptions);
                if (parsed != null && parsed.Count > 0)
                    return parsed;
            }

            // Fallback: try to parse as direct array
            var trimmed = llmResponse.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(trimmed, JsonOptions);
                if (parsed != null && parsed.Count > 0)
                    return parsed;
            }
        }
        catch
        {
            // Fall through to return single intent
        }

        return [];
    }
}