using System.Text.Json.Serialization;

namespace FinFlow.Infrastructure.Ocr.Groq;

internal sealed record GroqChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<GroqChatMessage> Messages,
    [property: JsonPropertyName("temperature")] int Temperature = 0);

internal sealed record GroqChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] object Content);

internal sealed record GroqTextContentPart(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);

internal sealed record GroqImageContentPart(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("image_url")] GroqImageUrl ImageUrl);

internal sealed record GroqImageUrl(
    [property: JsonPropertyName("url")] string Url);

internal sealed record GroqChatCompletionResponse(
    [property: JsonPropertyName("choices")] IReadOnlyList<GroqChatChoice>? Choices);

internal sealed record GroqChatChoice(
    [property: JsonPropertyName("message")] GroqChatResponseMessage? Message);

internal sealed record GroqChatResponseMessage(
    [property: JsonPropertyName("content")] string? Content);
