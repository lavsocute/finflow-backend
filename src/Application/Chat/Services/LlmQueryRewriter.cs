using System.Text;
using System.Text.Json;
using FinFlow.Application.Chat.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// LLM-based query rewriter. Calls a small model to convert relative dates and
/// implicit context into an explicit standalone search query before embedding.
///
/// Design choices:
/// - 2-second timeout. On failure or timeout, returns original query (fail-open).
/// - Reuses the same provider (Groq) but with a smaller cheaper model.
/// - Output is sanitized via <see cref="ChatPromptSanitizer"/> before return.
/// </summary>
public sealed class LlmQueryRewriter : IQueryRewriter
{
    private const int RewriteTimeoutSeconds = 2;
    private const int MaxRewrittenQueryLength = 500;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly QueryRewriterOptions _options;
    private readonly ILogger<LlmQueryRewriter> _logger;
    private readonly Uri _completionsUri;

    public LlmQueryRewriter(
        HttpClient httpClient,
        IOptions<QueryRewriterOptions> options,
        ILogger<LlmQueryRewriter> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? throw new InvalidOperationException("Query rewriter base URL is not configured.")
            : _options.BaseUrl.TrimEnd('/') + "/";
        _completionsUri = new Uri(new Uri(baseUrl, UriKind.Absolute), "chat/completions");
    }

    public async Task<string> RewriteAsync(string originalQuery, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(originalQuery))
            return originalQuery ?? string.Empty;

        if (!_options.Enabled)
            return originalQuery;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(RewriteTimeoutSeconds));

        try
        {
            var systemPrompt = $"You are a query rewriter. Rewrite the user's query into a standalone search query suitable for semantic search over an expense database. Resolve relative dates using current UTC date {DateTime.UtcNow:yyyy-MM-dd}. Output ONLY the rewritten query, no preamble.";
            var requestBody = new
            {
                model = _options.Model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = originalQuery }
                },
                temperature = 0.0,
                max_tokens = 200
            };

            var json = JsonSerializer.Serialize(requestBody, SerializerOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_completionsUri, content, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Query rewriter returned non-success status {Status}; falling back to original query.", response.StatusCode);
                return originalQuery;
            }

            var responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            using var doc = JsonDocument.Parse(responseText);

            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var contentEl) &&
                contentEl.ValueKind == JsonValueKind.String)
            {
                var rawRewritten = contentEl.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(rawRewritten))
                {
                    // Apply sanitization to defend against LLM-generated injection attempts
                    var rewritten = ChatPromptSanitizer.Sanitize(rawRewritten);

                    // Enforce length limit to prevent oversized LLM outputs
                    if (rewritten.Length > MaxRewrittenQueryLength)
                    {
                        _logger.LogWarning(
                            "Query rewriter output exceeded {MaxLength} chars ({ActualLength}); truncating.",
                            MaxRewrittenQueryLength, rewritten.Length);
                        rewritten = rewritten[..MaxRewrittenQueryLength];
                    }

                    // Validate rewritten query doesn't bypass intent: reject if it removes
                    // original intent markers (e.g., original had expense keywords but rewritten doesn't)
                    if (!QueryIntentValidator.IsCompatible(originalQuery, rewritten))
                    {
                        _logger.LogWarning(
                            "Query rewriter output intent mismatch; falling back to original query. Original: '{Original}', Rewritten: '{Rewritten}'",
                            originalQuery, rewritten);
                        return originalQuery;
                    }

                    _logger.LogDebug("Query rewritten: '{Original}' -> '{Rewritten}'", originalQuery, rewritten);
                    return rewritten;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Query rewriter timed out after {TimeoutSeconds}s; falling back to original query.", RewriteTimeoutSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query rewriter failed; falling back to original query.");
        }

        return originalQuery;
    }
}

public sealed class QueryRewriterOptions
{
    public bool Enabled { get; set; } = false;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    public string Model { get; set; } = "llama-3.1-8b-instant";
}
