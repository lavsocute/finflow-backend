using FinFlow.Domain.Chat;

namespace FinFlow.Application.Chat.Interfaces;

/// <summary>
/// LLM-based entity extractor for context resolution.
/// Handles follow-up detection, entity extraction, and entity resolution using AI.
/// </summary>
public interface ILlmEntityExtractor
{
    /// <summary>
    /// Detects if the query is a follow-up question using LLM analysis.
    /// </summary>
    Task<FollowUpDetectionResult> DetectFollowUpAsync(
        string query,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);

    /// <summary>
    /// Extracts entities from the query using LLM-based NER.
    /// </summary>
    Task<IReadOnlyList<ExtractedEntity>> ExtractEntitiesAsync(
        string query,
        CancellationToken ct = default);

    Task<IReadOnlyList<ExtractedEntity>> ExtractEntitiesAsync(
        string query,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves entity references within the query using LLM context understanding.
    /// </summary>
    Task<IReadOnlyList<EntityResolution>> ResolveEntityReferencesAsync(
        string query,
        ConversationContext context,
        IReadOnlyList<ExtractedEntity> entities,
        CancellationToken ct = default);

    Task<IReadOnlyList<EntityResolution>> ResolveEntityReferencesAsync(
        string query,
        ConversationContext context,
        IReadOnlyList<ExtractedEntity> entities,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);
}

public class FollowUpDetectionResult
{
    public bool IsFollowUp { get; init; }
    public float Confidence { get; init; }
    public string? FollowUpType { get; init; }
    public string? Reasoning { get; init; }
}

public sealed class LlmExtractedEntity : ExtractedEntity
{
}
