using System.Globalization;
using System.Text;
using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Infrastructure.Chat;

/// <summary>
/// Fully local, deterministic embedding service — NO external API calls.
///
/// Produces a fixed-dimension vector using the hashing trick over word unigrams and
/// bigrams (after Vietnamese diacritics folding), weighted by sublinear term
/// frequency and L2-normalized. Because BOTH indexing and query embedding go through
/// this same implementation, the vectors live in a single self-consistent space, so
/// cosine similarity is meaningful for lexical/near-lexical overlap.
///
/// This is intended for offline/seeded environments where calling a paid embedding
/// provider for thousands of documents is impractical. Semantic quality is weaker
/// than a neural embedder, but combined with the hybrid keyword (FTS) + BM25 rerank
/// pipeline it yields working RAG retrieval with zero API usage.
/// </summary>
public sealed class LocalHashingEmbeddingService : IEmbeddingService
{
    private readonly int _dimensions;

    public LocalHashingEmbeddingService(int dimensions = 2048)
    {
        if (dimensions <= 0)
            throw new ArgumentOutOfRangeException(nameof(dimensions));
        _dimensions = dimensions;
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        => Task.FromResult(Embed(text));

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        IReadOnlyList<float[]> result = texts.Select(Embed).ToList();
        return Task.FromResult(result);
    }

    private float[] Embed(string text)
    {
        var vector = new float[_dimensions];
        if (string.IsNullOrWhiteSpace(text))
            return vector;

        var tokens = Tokenize(text);
        if (tokens.Count == 0)
            return vector;

        // Raw term frequency over unigrams + bigrams via the hashing trick.
        var counts = new Dictionary<int, float>();

        void Add(string feature)
        {
            var hash = StableHash(feature);
            var index = (int)(hash % (uint)_dimensions);
            // Signed hashing reduces collision bias.
            var sign = ((hash >> 31) & 1) == 0 ? 1f : -1f;
            counts[index] = counts.GetValueOrDefault(index) + sign;
        }

        for (var i = 0; i < tokens.Count; i++)
        {
            Add(tokens[i]);
            if (i + 1 < tokens.Count)
                Add(tokens[i] + "_" + tokens[i + 1]);
        }

        // Sublinear scaling + L2 normalize.
        // Guard: signed-hash collisions can cancel to magnitude 0; Math.Log(0) is
        // -Infinity and Math.Sign(0)*... yields NaN, which breaks JSON/pgvector.
        double norm = 0;
        foreach (var kv in counts)
        {
            var magnitude = Math.Abs(kv.Value);
            if (magnitude <= 0)
                continue;

            var weighted = Math.Sign(kv.Value) * (1.0 + Math.Log(magnitude));
            if (double.IsNaN(weighted) || double.IsInfinity(weighted))
                continue;

            vector[kv.Key] = (float)weighted;
            norm += weighted * weighted;
        }

        if (norm > 0)
        {
            var inv = (float)(1.0 / Math.Sqrt(norm));
            for (var i = 0; i < vector.Length; i++)
                if (vector[i] != 0f)
                    vector[i] *= inv;
        }

        return vector;
    }

    private static List<string> Tokenize(string text)
    {
        var folded = RemoveDiacritics(text).ToLowerInvariant();
        var tokens = new List<string>();
        var builder = new StringBuilder();

        foreach (var ch in folded)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0)
            {
                tokens.Add(builder.ToString());
                builder.Clear();
            }
        }
        if (builder.Length > 0)
            tokens.Add(builder.ToString());

        return tokens;
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(ch switch { 'đ' => 'd', 'Đ' => 'D', _ => ch });
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    // FNV-1a 32-bit — stable across processes (unlike string.GetHashCode).
    private static uint StableHash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= prime;
        }
        return hash;
    }
}