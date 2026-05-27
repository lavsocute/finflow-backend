namespace FinFlow.Application.Chat.Interfaces;

/// <summary>
/// Detects and splits queries with multiple intents into separate sub-queries.
/// Uses LLM to intelligently identify conjunction boundaries.
/// </summary>
public interface IMultiIntentDetector
{
    /// <summary>
    /// Analyzes the query and returns a list of sub-queries if multiple intents are detected.
    /// Returns a single-element list with the original query if only one intent is found.
    /// </summary>
    Task<IReadOnlyList<string>> DetectAndSplitAsync(string query, CancellationToken ct = default);
}