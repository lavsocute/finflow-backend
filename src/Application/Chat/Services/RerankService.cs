using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Documents;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Reranks retrieved document chunks using BM25 (Okapi BM25) — the industry-standard
/// text relevance algorithm used by Lucene/Elasticsearch. Significantly more accurate
/// than naive keyword overlap because it accounts for:
///   - Term frequency saturation (k1) — diminishing returns for repeated matches
///   - Document length normalization (b) — shorter chunks aren't penalized
///   - Inverse document frequency (idf) — rare terms weight more than common ones
///
/// For production-grade RAG, consider replacing with a cross-encoder reranker (e.g.,
/// Cohere Rerank API, BGE-Reranker). BM25 is a strong deterministic baseline.
/// </summary>
public sealed class RerankService : IRerankService
{
    // BM25 parameters — standard Lucene defaults.
    private const float K1 = 1.5f;
    private const float B = 0.75f;

    private static readonly char[] WordSeparators = [' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '(', ')', '[', ']', '{', '}', '"', '\''];

    public Task<IReadOnlyList<(DocumentChunk Chunk, float Score)>> RerankAsync(
        string query,
        IEnumerable<DocumentChunk> chunks,
        int topN = 5,
        CancellationToken ct = default)
    {
        var chunkList = chunks.ToList();
        if (chunkList.Count == 0)
            return Task.FromResult<IReadOnlyList<(DocumentChunk, float)>>([]);

        var queryTerms = Tokenize(query);
        if (queryTerms.Count == 0)
        {
            // Empty query — return original order.
            var fallback = chunkList.Take(topN).Select(c => (c, 0f)).ToList();
            return Task.FromResult<IReadOnlyList<(DocumentChunk, float)>>(fallback);
        }

        // Tokenize all chunks once.
        var tokenizedChunks = chunkList
            .Select(c => (Chunk: c, Terms: Tokenize(c.Content)))
            .ToList();

        // Compute average document length (in tokens).
        var avgDocLength = tokenizedChunks.Count == 0
            ? 0
            : tokenizedChunks.Average(t => (double)t.Terms.Count);

        // Compute IDF for each query term. IDF = log((N - df + 0.5) / (df + 0.5) + 1).
        var totalDocs = tokenizedChunks.Count;
        var idf = new Dictionary<string, double>();
        foreach (var term in queryTerms.Distinct())
        {
            var df = tokenizedChunks.Count(t => t.Terms.Contains(term));
            idf[term] = Math.Log(((totalDocs - df + 0.5) / (df + 0.5)) + 1.0);
        }

        // Score each chunk.
        var results = tokenizedChunks
            .Select(t => (Chunk: t.Chunk, Score: ComputeBm25Score(queryTerms, t.Terms, idf, avgDocLength)))
            .OrderByDescending(x => x.Score)
            .Take(topN)
            .ToList();

        return Task.FromResult<IReadOnlyList<(DocumentChunk, float)>>(
            results.Select(r => (r.Chunk, (float)r.Score)).ToList());
    }

    private static double ComputeBm25Score(
        IReadOnlyList<string> queryTerms,
        IReadOnlyList<string> docTerms,
        IReadOnlyDictionary<string, double> idf,
        double avgDocLength)
    {
        if (docTerms.Count == 0) return 0;

        // Count term frequency in the document.
        var termFreq = new Dictionary<string, int>();
        foreach (var term in docTerms)
        {
            termFreq[term] = termFreq.TryGetValue(term, out var count) ? count + 1 : 1;
        }

        double score = 0;
        var docLength = (double)docTerms.Count;
        var lengthNorm = K1 * (1 - B + B * (docLength / avgDocLength));

        foreach (var queryTerm in queryTerms.Distinct())
        {
            if (!termFreq.TryGetValue(queryTerm, out var tf)) continue;
            if (!idf.TryGetValue(queryTerm, out var termIdf)) continue;

            score += termIdf * (tf * (K1 + 1)) / (tf + lengthNorm);
        }

        return score;
    }

    private static List<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        return text
            .ToLowerInvariant()
            .Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1) // Skip single-char tokens (a, i, ...).
            .ToList();
    }
}
