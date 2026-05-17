namespace FinFlow.Application.Chat.Interfaces;

/// <summary>
/// Post-processes LLM responses to detect and redact sensitive content
/// (PII, system prompt leaks, instruction echoes) before they reach the user.
/// </summary>
public interface IChatOutputFilter
{
    /// <summary>
    /// Sanitize <paramref name="rawResponse"/> and return both cleaned text
    /// and metadata about what was redacted (for audit logging).
    /// </summary>
    ChatOutputFilterResult Sanitize(string rawResponse);
}

/// <summary>
/// Result of running the output filter on an LLM response.
/// </summary>
/// <param name="SanitizedResponse">Response text safe to return to the user.</param>
/// <param name="RedactionCount">Number of distinct redactions applied.</param>
/// <param name="RedactionTypes">Categories triggered (e.g. "Email", "TaxId", "SystemPrompt").</param>
public sealed record ChatOutputFilterResult(
    string SanitizedResponse,
    int RedactionCount,
    IReadOnlyList<string> RedactionTypes);
