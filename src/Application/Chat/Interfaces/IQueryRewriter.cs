namespace FinFlow.Application.Chat.Interfaces;

/// <summary>
/// Optional preprocessor: rewrites the user query into a standalone search-friendly
/// form before embedding. Resolves relative dates, expands acronyms, etc.
/// </summary>
public interface IQueryRewriter
{
    /// <summary>
    /// Returns the rewritten query, or the original if rewriting is disabled or fails.
    /// MUST never throw — failure path falls back to original query.
    /// </summary>
    Task<string> RewriteAsync(string originalQuery, CancellationToken cancellationToken = default);
}
