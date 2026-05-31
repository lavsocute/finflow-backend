using FinFlow.Application.Chat.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinFlow.Application.Chat.Cascade;

/// <summary>
/// In-memory snapshot of intent exemplars with precomputed L2-normalised embeddings.
/// Refreshes on demand from the repository (DB) at startup or via background sync.
/// Cosine similarity reduces to a dot product after normalisation; ranking 200 exemplars
/// at D=2048 stays well under 1ms.
/// </summary>
public sealed class IntentExemplarRegistry
{
    private readonly object _gate = new();
    private NormalisedExemplar[] _exemplars = Array.Empty<NormalisedExemplar>();
    private string _embeddingModel = string.Empty;
    private readonly ILogger<IntentExemplarRegistry> _logger;

    public IntentExemplarRegistry(ILogger<IntentExemplarRegistry>? logger = null)
    {
        _logger = logger ?? NullLogger<IntentExemplarRegistry>.Instance;
    }

    public int Count => _exemplars.Length;
    public string EmbeddingModel => _embeddingModel;

    public void Replace(IEnumerable<IntentExemplarMaterial> material, string embeddingModel)
    {
        var snapshot = material
            .Select(m => new NormalisedExemplar(
                m.Id,
                m.Text,
                m.Mode,
                m.Family,
                m.Task,
                Normalise(m.Embedding),
                Math.Max(0.1, m.Weight)))
            .ToArray();

        lock (_gate)
        {
            _exemplars = snapshot;
            _embeddingModel = embeddingModel;
        }

        _logger.LogInformation(
            "IntentExemplarRegistry loaded {Count} exemplars for model {Model}.",
            snapshot.Length, embeddingModel);
    }

    public IReadOnlyList<EmbeddingIntentMatch> Rank(float[] queryEmbedding, int topK)
    {
        if (queryEmbedding is null || queryEmbedding.Length == 0)
            return Array.Empty<EmbeddingIntentMatch>();

        var snapshot = _exemplars;
        if (snapshot.Length == 0)
            return Array.Empty<EmbeddingIntentMatch>();

        var queryNorm = Normalise(queryEmbedding);
        var scored = new (double score, NormalisedExemplar ex)[snapshot.Length];
        for (var i = 0; i < snapshot.Length; i++)
        {
            var ex = snapshot[i];
            if (ex.Embedding.Length != queryNorm.Length)
            {
                scored[i] = (-1.0, ex);
                continue;
            }
            var dot = Dot(queryNorm, ex.Embedding);
            // weight slight: high-weight exemplars edge out near-tie peers.
            scored[i] = (dot * (0.85 + 0.15 * ex.Weight), ex);
        }

        Array.Sort(scored, static (a, b) => b.score.CompareTo(a.score));

        var take = Math.Min(topK, scored.Length);
        var result = new List<EmbeddingIntentMatch>(take);
        for (var i = 0; i < take; i++)
        {
            var (score, ex) = scored[i];
            result.Add(new EmbeddingIntentMatch(
                ex.Id.ToString(),
                ex.Text,
                ex.Mode,
                ex.Family,
                ex.Task,
                Math.Clamp(score, -1.0, 1.0)));
        }
        return result;
    }

    private static float[] Normalise(float[] source)
    {
        if (source.Length == 0)
            return source;

        double sumSq = 0.0;
        for (var i = 0; i < source.Length; i++)
            sumSq += (double)source[i] * source[i];

        if (sumSq <= double.Epsilon)
            return source;

        var inv = (float)(1.0 / Math.Sqrt(sumSq));
        var output = new float[source.Length];
        for (var i = 0; i < source.Length; i++)
            output[i] = source[i] * inv;
        return output;
    }

    private static double Dot(float[] a, float[] b)
    {
        double sum = 0.0;
        for (var i = 0; i < a.Length; i++)
            sum += (double)a[i] * b[i];
        return sum;
    }

    private readonly record struct NormalisedExemplar(
        Guid Id,
        string Text,
        ChatExecutionMode Mode,
        ChatIntentFamily Family,
        ChatReportingTask Task,
        float[] Embedding,
        double Weight);
}

public sealed record IntentExemplarMaterial(
    Guid Id,
    string Text,
    ChatExecutionMode Mode,
    ChatIntentFamily Family,
    ChatReportingTask Task,
    float[] Embedding,
    double Weight);
