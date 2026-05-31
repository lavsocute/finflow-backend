using System.Security.Cryptography;
using System.Text;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Decorator that caches embedding results to avoid redundant provider calls
/// for identical text (queries, document chunks). Embeddings are content-based
/// so cache key omits tenant/user — same text always maps to the same vector.
/// </summary>
public sealed class CachingEmbeddingService : IEmbeddingService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private const string CacheKeyPrefix = "embedding:";

    private readonly IEmbeddingService _inner;
    private readonly ICacheService _cache;
    private readonly ILogger<CachingEmbeddingService> _logger;
    private readonly string _spaceId;

    public CachingEmbeddingService(
        IEmbeddingService inner,
        ICacheService cache,
        ILogger<CachingEmbeddingService> logger,
        string? embeddingSpaceId = null)
    {
        _inner = inner;
        _cache = cache;
        _logger = logger;
        // Distinguishes embedding spaces (e.g. local-hashing vs a specific neural model).
        // WITHOUT this, switching providers returns stale vectors from the prior space
        // because the cache key was content-only — cross-space cache poisoning.
        _spaceId = string.IsNullOrWhiteSpace(embeddingSpaceId) ? "default" : embeddingSpaceId;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return await _inner.EmbedAsync(text, ct);

        var key = BuildKey(text);
        EmbeddingCacheEntry? cached = null;
        try
        {
            cached = await _cache.GetAsync<EmbeddingCacheEntry>(key, ct);
        }
        catch (Exception ex)
        {
            // Schema drift in cached entries (e.g., older record with missing fields) should not break requests.
            _logger.LogWarning(ex, "Embedding cache read failed for key {Key}; treating as miss.", key);
        }
        if (cached is { Vector.Length: > 0 })
        {
            _logger.LogDebug("Embedding cache HIT for key {Key}", key);
            return cached.Vector;
        }

        _logger.LogDebug("Embedding cache MISS for key {Key}", key);
        var embedding = await _inner.EmbedAsync(text, ct);
        if (embedding is { Length: > 0 })
        {
            await _cache.SetAsync(key, new EmbeddingCacheEntry(embedding), CacheTtl, ct);
        }
        return embedding;
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        // Cache-aware batching: split into hits + misses, only call provider for misses,
        // then re-merge preserving original order.
        var inputList = texts.ToList();
        var resultArray = new float[inputList.Count][];
        var missIndices = new List<int>();
        var missTexts = new List<string>();
        var missKeys = new List<string>();

        for (int i = 0; i < inputList.Count; i++)
        {
            var text = inputList[i];
            if (string.IsNullOrWhiteSpace(text))
            {
                resultArray[i] = Array.Empty<float>();
                continue;
            }
            var key = BuildKey(text);
            var cached = await _cache.GetAsync<EmbeddingCacheEntry>(key, ct);
            if (cached is { Vector.Length: > 0 })
            {
                resultArray[i] = cached.Vector;
            }
            else
            {
                missIndices.Add(i);
                missTexts.Add(text);
                missKeys.Add(key);
            }
        }

        if (missTexts.Count > 0)
        {
            var providerResults = await _inner.EmbedBatchAsync(missTexts, ct);

            // Provider must return exactly one embedding per requested miss.
            // If counts diverge (provider dropped or merged), fail fast — re-mapping
            // by index would silently associate the wrong embedding with the wrong text.
            if (providerResults.Count != missTexts.Count)
            {
                _logger.LogError(
                    "Embedding provider returned {ReturnedCount} vectors for {RequestedCount} inputs; aborting batch.",
                    providerResults.Count, missTexts.Count);
                throw new InvalidOperationException(
                    $"Embedding provider returned {providerResults.Count} vectors for {missTexts.Count} inputs; expected exact 1:1 mapping.");
            }

            for (int j = 0; j < providerResults.Count; j++)
            {
                var vec = providerResults[j];
                resultArray[missIndices[j]] = vec;
                if (vec is { Length: > 0 })
                {
                    await _cache.SetAsync(missKeys[j], new EmbeddingCacheEntry(vec), CacheTtl, ct);
                }
            }
        }

        _logger.LogDebug(
            "Embedding batch: {Total} requested, {Hits} hits, {Misses} misses",
            inputList.Count, inputList.Count - missTexts.Count, missTexts.Count);

        return resultArray;
    }

    private string BuildKey(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return CacheKeyPrefix + _spaceId + ":" + Convert.ToHexString(bytes);
    }

    private sealed record EmbeddingCacheEntry(float[] Vector);
}
