using FinFlow.Domain.Chat;

namespace FinFlow.Application.Chat.Interfaces;

/// <summary>
/// LLM-powered entity extraction service.
/// Uses function calling / tool calling pattern for structured extraction
/// of entities from user messages and conversation history.
/// </summary>
public interface IEntityExtractor
{
    /// <summary>
    /// Extracts entities from the given message and conversation history.
    /// </summary>
    Task<IReadOnlyList<ExtractedEntity>> ExtractAsync(
        string message,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);
}

/// <summary>
/// Represents an entity extracted from user input via LLM.
/// </summary>
public class ExtractedEntity
{
    public string Text { get; init; } = string.Empty;
    public EntityType Type { get; init; }
    public double Confidence { get; init; }
    public string? NormalizedForm { get; init; }
    public Dictionary<string, object>? Attributes { get; init; }

    public ExtractedEntity() { }

    public ExtractedEntity(string text, EntityType type, double confidence)
    {
        Text = text;
        Type = type;
        Confidence = confidence;
    }
}
