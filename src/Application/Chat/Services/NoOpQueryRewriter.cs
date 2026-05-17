using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Default rewriter that returns the original query unchanged.
/// Production deployments should swap with <c>LlmQueryRewriter</c>.
/// </summary>
public sealed class NoOpQueryRewriter : IQueryRewriter
{
    public Task<string> RewriteAsync(string originalQuery, CancellationToken cancellationToken = default)
        => Task.FromResult(originalQuery ?? string.Empty);
}
