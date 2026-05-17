using FinFlow.Domain.Documents;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Reciprocal Rank Fusion: combines ranked lists from multiple retrievers
/// (e.g. vector + keyword) into a single fused ranking.
///
/// Score for a chunk = Σ 1 / (k + rank_i), where rank_i is its 1-indexed rank
/// in retriever i. Chunks not present in a list contribute 0 from that retriever.
/// k = 60 is the canonical value from the original RRF paper (Cormack 2009).
/// </summary>
public static class ReciprocalRankFusion
{
    private const int RrfConstant = 60;

    public static IReadOnlyList<DocumentChunk> Fuse(
        IReadOnlyList<DocumentChunk> vectorResults,
        IReadOnlyList<DocumentChunk> keywordResults,
        int topK)
    {
        var scores = new Dictionary<Guid, double>();
        var byId = new Dictionary<Guid, DocumentChunk>();

        AddRanked(vectorResults, scores, byId);
        AddRanked(keywordResults, scores, byId);

        return scores
            .OrderByDescending(kv => kv.Value)
            .Take(topK)
            .Select(kv => byId[kv.Key])
            .ToList();
    }

    private static void AddRanked(
        IReadOnlyList<DocumentChunk> ranked,
        Dictionary<Guid, double> scores,
        Dictionary<Guid, DocumentChunk> byId)
    {
        for (int rank = 0; rank < ranked.Count; rank++)
        {
            var chunk = ranked[rank];
            if (!byId.ContainsKey(chunk.Id))
                byId[chunk.Id] = chunk;

            var contribution = 1.0 / (RrfConstant + rank + 1); // 1-indexed rank
            scores[chunk.Id] = scores.TryGetValue(chunk.Id, out var existing)
                ? existing + contribution
                : contribution;
        }
    }
}
