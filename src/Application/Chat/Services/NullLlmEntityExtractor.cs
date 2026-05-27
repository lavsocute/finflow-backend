using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Chat;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// A no-op implementation of ILlmEntityExtractor for cases where LLM extraction is not needed.
/// </summary>
public sealed class NullLlmEntityExtractor : ILlmEntityExtractor
{
    public static readonly NullLlmEntityExtractor Instance = new();

    private NullLlmEntityExtractor() { }

    public Task<FollowUpDetectionResult> DetectFollowUpAsync(
        string query,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default)
    {
        return Task.FromResult(new FollowUpDetectionResult
        {
            IsFollowUp = false,
            Confidence = 0f,
            FollowUpType = null,
            Reasoning = "Null extractor"
        });
    }

    public Task<IReadOnlyList<ExtractedEntity>> ExtractEntitiesAsync(
        string query,
        IReadOnlyList<ChatMessage> history = null!,
        CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<ExtractedEntity>>([]);
    }

    public Task<IReadOnlyList<ExtractedEntity>> ExtractEntitiesAsync(
        string query,
        CancellationToken ct)
    {
        return ExtractEntitiesAsync(query, [], ct);
    }

    public Task<IReadOnlyList<EntityResolution>> ResolveEntityReferencesAsync(
        string query,
        ConversationContext context,
        IReadOnlyList<ExtractedEntity> entities,
        IReadOnlyList<ChatMessage> history = null!,
        CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<EntityResolution>>([]);
    }

    public Task<IReadOnlyList<EntityResolution>> ResolveEntityReferencesAsync(
        string query,
        ConversationContext context,
        IReadOnlyList<ExtractedEntity> entities,
        CancellationToken ct)
    {
        return ResolveEntityReferencesAsync(query, context, entities, [], ct);
    }
}
