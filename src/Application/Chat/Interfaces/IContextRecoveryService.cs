using FinFlow.Domain.Chat;

namespace FinFlow.Application.Chat.Interfaces;

/// <summary>
/// Provides graceful degradation when LLM extraction fails or returns low confidence.
/// Falls back to conversation history analysis and generates natural Vietnamese clarification prompts.
/// </summary>
public interface IContextRecoveryService
{
    /// <summary>
    /// Attempts to recover context from conversation history when LLM is unavailable or extraction failed.
    /// </summary>
    Task<ContextRecoveryResult> RecoverFromHistoryAsync(
        Guid sessionId,
        IReadOnlyList<ChatMessage> history,
        int messageCount = 5,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a natural Vietnamese clarification prompt when confidence is low or entity is unknown.
    /// </summary>
    string GenerateClarificationPrompt(
        string query,
        ClarificationType type,
        IReadOnlyList<ChatMessage>? history = null);

    /// <summary>
    /// Analyzes conversation history to infer the user's intent when LLM extraction fails.
    /// </summary>
    Task<IntentInferenceResult> InferIntentFromHistoryAsync(
        Guid sessionId,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if the current query can be resolved with partial context.
    /// </summary>
    bool CanResolveWithPartialContext(string query, ConversationContext? context);
}

/// <summary>
/// Type of clarification needed for generating appropriate prompts.
/// </summary>
public enum ClarificationType
{
    /// <summary>
    /// LLM returned low confidence score.
    /// </summary>
    LowConfidence,

    /// <summary>
    /// Entity could not be identified.
    /// </summary>
    EntityUnknown,

    /// <summary>
    /// User intent is unclear from the query.
    /// </summary>
    IntentUnclear,

    /// <summary>
    /// Multiple entities match the query criteria.
    /// </summary>
    MultipleMatches,

    /// <summary>
    /// Reference is ambiguous (e.g., "that one", "the department").
    /// </summary>
    AmbiguousReference
}

/// <summary>
/// Strategy used for context recovery.
/// </summary>
public enum RecoveryStrategy
{
    /// <summary>
    /// No recovery was possible.
    /// </summary>
    None,

    /// <summary>
    /// Context was recovered from conversation history analysis.
    /// </summary>
    HistoryAnalysis,

    /// <summary>
    /// Context was recovered by combining cached context with partial matches.
    /// </summary>
    PartialContextFusion,

    /// <summary>
    /// User was asked for clarification which was then used.
    /// </summary>
    UserClarification
}

/// <summary>
/// Result of context recovery attempt from conversation history.
/// </summary>
public class ContextRecoveryResult
{
    public bool Success { get; init; }
    public RecoveryStrategy RecoveryStrategy { get; init; }
    public List<TrackedEntity> RecoveredEntities { get; init; } = [];
    public string? InferredIntent { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Result of intent inference from conversation history.
/// </summary>
public class IntentInferenceResult
{
    public bool Success { get; init; }
    public string? InferredIntent { get; init; }
    public string? InferredTopic { get; init; }
    public float Confidence { get; init; }
    public string Reasoning { get; init; } = string.Empty;
}