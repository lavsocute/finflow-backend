using FinFlow.Application.Chat.Interfaces;
using System.Net;
using System.Text.Json.Serialization;

namespace FinFlow.Application.Chat.Interfaces;

/// <summary>
/// Abstraction for LLM chat providers (Groq, OpenAI, Azure OpenAI, etc.)
/// </summary>
public interface ILlmChatService
{
    /// <summary>
    /// Sends a chat completion request and returns the full response.
    /// </summary>
    Task<LlmChatResult> ChatAsync(LlmChatRequest request, CancellationToken ct = default);

    /// <summary>
    /// Sends a chat completion request and streams the response.
    /// </summary>
    IAsyncEnumerable<LlmStreamEvent> ChatStreamAsync(LlmChatRequest request, CancellationToken ct = default);
}

/// <summary>
/// Provider-agnostic chat request for LLM services.
/// </summary>
public record LlmChatRequest(
    string System,
    IReadOnlyList<LlmMessage> Messages,
    string? Model = null,
    double? Temperature = null,
    int? MaxTokens = null,
    LlmResponseFormat? ResponseFormat = null
);

public sealed record LlmResponseFormat(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("json_schema")] LlmJsonSchemaResponseFormat? JsonSchema)
{
    public static LlmResponseFormat ForJsonObject() =>
        new("json_object", null);

    public static LlmResponseFormat ForJsonSchema(string name, object schema, bool strict = true) =>
        new("json_schema", new LlmJsonSchemaResponseFormat(name, schema, Strict: strict));
}

public sealed record LlmJsonSchemaResponseFormat(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("schema")] object Schema,
    [property: JsonPropertyName("strict")] bool Strict);

/// <summary>
/// A chat message in an LLM request.
/// </summary>
public record LlmMessage(
    string Role,
    string Content
);

/// <summary>
/// Result from a non-streaming LLM chat call.
/// </summary>
public record LlmChatResult(
    string Content,
    int? TotalTokens = null,
    int? PromptTokens = null,
    int? CompletionTokens = null
);

public sealed class LlmProviderException : InvalidOperationException
{
    public LlmProviderException(
        string message,
        HttpStatusCode? statusCode = null,
        string responseBody = "",
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode? StatusCode { get; }

    public string ResponseBody { get; }

    public bool IsSchemaFailure =>
        ResponseBody.Contains("json_validate_failed", StringComparison.OrdinalIgnoreCase) ||
        ResponseBody.Contains("response_format", StringComparison.OrdinalIgnoreCase) ||
        Message.Contains("json_validate_failed", StringComparison.OrdinalIgnoreCase) ||
        Message.Contains("response_format", StringComparison.OrdinalIgnoreCase);

    public bool IsRateLimit =>
        StatusCode == HttpStatusCode.TooManyRequests ||
        ResponseBody.Contains("rate_limit", StringComparison.OrdinalIgnoreCase) ||
        Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Stream event from a streaming LLM chat call.
/// </summary>
public enum LlmStreamEventKind
{
    Token,
    Usage,
    Done
}

/// <summary>
/// A stream event containing a token chunk or usage data.
/// </summary>
public record LlmStreamEvent(
    LlmStreamEventKind Kind,
    string? Token = null,
    int? TotalTokens = null,
    int? PromptTokens = null,
    int? CompletionTokens = null
);
