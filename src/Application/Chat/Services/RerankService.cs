using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Documents;

namespace FinFlow.Application.Chat.Services;

public sealed class RerankService : IRerankService
{
    public Task<IReadOnlyList<(DocumentChunk Chunk, float Score)>> RerankAsync(
        string query,
        IEnumerable<DocumentChunk> chunks,
        int topN = 5,
        CancellationToken ct = default)
    {
        var chunkList = chunks.ToList();

        var results = chunkList
            .Select(c => (Chunk: c, Score: ComputeRelevance(query, c)))
            .OrderByDescending(x => x.Score)
            .Take(topN)
            .ToList();

        return Task.FromResult<IReadOnlyList<(DocumentChunk, float)>>(results);
    }

    private static float ComputeRelevance(string query, DocumentChunk chunk)
    {
        var queryTerms = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var content = chunk.Content.ToLower();

        int matches = queryTerms.Count(term => content.Contains(term));
        return (float)matches / queryTerms.Length;
    }
}