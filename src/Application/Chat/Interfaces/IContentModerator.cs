namespace FinFlow.Application.Chat.Interfaces;

/// <summary>
/// Pre-LLM moderation: classify user query and reject content that violates
/// platform policy (hate speech, threats, NSFW, etc.) before incurring LLM cost.
/// </summary>
public interface IContentModerator
{
    /// <summary>
    /// Returns null if the content is allowed, or a non-empty reason category
    /// (e.g. "hate", "threat", "nsfw") if it should be rejected.
    /// </summary>
    string? Moderate(string query);
}
