using FinFlow.Application.Chat.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Groq-based implementation of ILlmChatService for multi-intent detection and other LLM operations.
/// </summary>
public sealed class GroqLlmChatService : ILlmChatService
{
    private readonly HttpClient _httpClient;
    private readonly GroqChatOptions _options;
    private readonly ILogger<GroqLlmChatService> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GroqLlmChatService(
        HttpClient httpClient,
        IOptions<GroqChatOptions> options,
        ILogger<GroqLlmChatService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LlmChatResult> ChatAsync(LlmChatRequest request, CancellationToken ct = default)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.System))
            messages.Add(new { role = "system", content = request.System });
        foreach (var msg in request.Messages)
            messages.Add(new { role = msg.Role, content = msg.Content });

        var requestBody = new
        {
            model = request.Model ?? _options.ChatModel,
            messages,
            temperature = request.Temperature ?? 0.1,
            max_tokens = request.MaxTokens ?? 500,
            response_format = request.ResponseFormat
        };

        var jsonContent = JsonSerializer.Serialize(requestBody, SerializerOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUri());
        httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Groq LLM chat returned {StatusCode}: {Error}", response.StatusCode, error);
            throw new LlmProviderException(
                $"Groq API error: {response.StatusCode}",
                response.StatusCode,
                error);
        }

        var responseText = await response.Content.ReadAsStringAsync(ct);
        var json = JsonSerializer.Deserialize<GroqChatResponse>(responseText, SerializerOptions);

        if (json?.Choices == null || json.Choices.Count == 0)
            throw new InvalidOperationException("No response from Groq");

        return new LlmChatResult(
            json.Choices[0].Message.Content,
            json.Usage?.TotalTokens,
            json.Usage?.PromptTokens,
            json.Usage?.CompletionTokens);
    }

    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamAsync(
        LlmChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.System))
            messages.Add(new { role = "system", content = request.System });
        foreach (var msg in request.Messages)
            messages.Add(new { role = msg.Role, content = msg.Content });

        var requestBody = new
        {
            model = request.Model ?? _options.ChatModel,
            messages,
            temperature = request.Temperature ?? 0.1,
            max_tokens = request.MaxTokens ?? 500,
            stream = true
        };

        var jsonContent = JsonSerializer.Serialize(requestBody, SerializerOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUri());
        httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Groq LLM stream returned {StatusCode}: {Error}", response.StatusCode, error);
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var payload = line.Substring(5).Trim();
            if (payload == "[DONE]")
            {
                yield return new LlmStreamEvent(LlmStreamEventKind.Done);
                yield break;
            }

            string? token = null;
            int? usageTokens = null;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var contentEl) &&
                    contentEl.ValueKind == JsonValueKind.String)
                {
                    token = contentEl.GetString();
                }

                if (root.TryGetProperty("usage", out var usage) &&
                    usage.TryGetProperty("total_tokens", out var totalTokensEl) &&
                    totalTokensEl.ValueKind == JsonValueKind.Number)
                {
                    usageTokens = totalTokensEl.GetInt32();
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse SSE payload: {Payload}", payload);
            }

            if (!string.IsNullOrEmpty(token))
                yield return new LlmStreamEvent(LlmStreamEventKind.Token, token);
            if (usageTokens.HasValue)
                yield return new LlmStreamEvent(LlmStreamEventKind.Usage, TotalTokens: usageTokens.Value);
        }
    }

    private Uri BuildChatCompletionsUri()
    {
        return ChatCompletionsEndpointBuilder.Build(_options.BaseUrl);
    }

    private record GroqChatResponse(
        List<GroqChoice> Choices,
        GroqUsage? Usage
    );

    private record GroqChoice(
        GroqMessage Message
    );

    private record GroqMessage(
        string Content
    );

    private record GroqUsage(
        int? PromptTokens,
        int? CompletionTokens,
        int? TotalTokens
    );
}
