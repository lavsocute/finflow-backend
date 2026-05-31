using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Application.Chat.Cascade;

public sealed class EmbeddingIntentClassifier : IIntentEmbeddingClassifier
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IntentExemplarRegistry _registry;

    public EmbeddingIntentClassifier(
        IEmbeddingService embeddingService,
        IntentExemplarRegistry registry)
    {
        _embeddingService = embeddingService;
        _registry = registry;
    }

    public async Task<IReadOnlyList<EmbeddingIntentMatch>> RankAsync(
        string query,
        int topK,
        CancellationToken ct)
    {
        // NOTE: embed RAW query (with diacritics) — exemplars are seeded raw and the neural
        // model is diacritic-sensitive. Do NOT pass diacritic-stripped text here.
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<EmbeddingIntentMatch>();
        if (_registry.Count == 0)
            return Array.Empty<EmbeddingIntentMatch>();

        var embedding = await _embeddingService.EmbedAsync(query, ct);
        if (embedding is null || embedding.Length == 0)
            return Array.Empty<EmbeddingIntentMatch>();

        return _registry.Rank(embedding, topK);
    }
}
