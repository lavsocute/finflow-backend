using FinFlow.Application.Chat.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinFlow.Application.Chat.Cascade;

/// <summary>
/// Synchronises the in-memory <see cref="IntentExemplarRegistry"/> with the durable
/// <see cref="IChatIntentExemplarRepository"/>. On first run for a given embedding model,
/// seeds the table from the embedded JSON via <see cref="IntentExemplarSeedLoader"/> and
/// computes embeddings via <see cref="IEmbeddingService"/>.
/// </summary>
public sealed class IntentExemplarSyncService
{
    private readonly IChatIntentExemplarRepository _repository;
    private readonly IEmbeddingService _embeddingService;
    private readonly IntentExemplarRegistry _registry;
    private readonly ILogger<IntentExemplarSyncService> _logger;

    public IntentExemplarSyncService(
        IChatIntentExemplarRepository repository,
        IEmbeddingService embeddingService,
        IntentExemplarRegistry registry,
        ILogger<IntentExemplarSyncService>? logger = null)
    {
        _repository = repository;
        _embeddingService = embeddingService;
        _registry = registry;
        _logger = logger ?? NullLogger<IntentExemplarSyncService>.Instance;
    }

    public async Task SyncAsync(string embeddingModelId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(embeddingModelId))
            throw new ArgumentException("Embedding model required.", nameof(embeddingModelId));

        var existing = await _repository.GetActiveAsync(embeddingModelId, tenantId: null, ct);

        if (existing.Count == 0)
        {
            _logger.LogInformation(
                "No exemplars found for model {Model}; seeding from embedded JSON.",
                embeddingModelId);
            existing = await SeedFromEmbeddedAsync(embeddingModelId, ct);
        }

        _registry.Replace(
            existing.Select(e => new IntentExemplarMaterial(
                e.Id,
                e.ExemplarText,
                e.IntentMode,
                e.IntentFamily,
                e.IntentTask,
                e.Embedding,
                e.Weight)),
            embeddingModelId);
    }

    private async Task<IReadOnlyList<ChatIntentExemplar>> SeedFromEmbeddedAsync(
        string embeddingModelId,
        CancellationToken ct)
    {
        var seeds = IntentExemplarSeedLoader.LoadEmbedded();
        if (seeds.Count == 0)
            return Array.Empty<ChatIntentExemplar>();

        var entities = new List<ChatIntentExemplar>(seeds.Count);
        foreach (var seed in seeds)
        {
            ct.ThrowIfCancellationRequested();
            var embedding = await _embeddingService.EmbedAsync(seed.Text, ct);
            if (embedding is null || embedding.Length == 0)
            {
                _logger.LogWarning(
                    "Embedding service returned empty for exemplar '{Text}' — skipped.",
                    seed.Text);
                continue;
            }

            entities.Add(ChatIntentExemplar.Create(
                seed.Text,
                seed.Language,
                seed.Mode,
                seed.Family,
                seed.Task,
                seed.Weight,
                embedding,
                embeddingModelId,
                idTenant: null));
        }

        await _repository.AddRangeAsync(entities, ct);

        _logger.LogInformation(
            "Seeded {Count} exemplars for model {Model}.",
            entities.Count, embeddingModelId);

        return entities;
    }
}
