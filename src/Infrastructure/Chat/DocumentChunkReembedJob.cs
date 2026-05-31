using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinFlow.Infrastructure.Chat;

/// <summary>
/// Maintenance job: regenerates the embedding vector for every DocumentChunk using the
/// currently-configured <see cref="IEmbeddingService"/>. Required when the embedding
/// provider/model changes (e.g. local-hashing -> neural), because query and chunk vectors
/// must live in the same embedding space for cosine similarity to be meaningful.
///
/// Runs outside the HTTP request pipeline (CLI), so it bypasses tenant query filters and
/// writes via raw ExecuteUpdate to avoid the multi-tenant SaveChanges guard.
/// </summary>
public sealed class DocumentChunkReembedJob
{
    private readonly ApplicationDbContext _db;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<DocumentChunkReembedJob> _logger;

    public DocumentChunkReembedJob(
        ApplicationDbContext db,
        IEmbeddingService embeddingService,
        ILogger<DocumentChunkReembedJob> logger)
    {
        _db = db;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task<int> RunAsync(int batchSize, CancellationToken ct)
    {
        if (!_db.Database.IsNpgsql())
        {
            _logger.LogWarning("Re-embed job requires Npgsql (pgvector). Skipping.");
            return 0;
        }

        var ids = await _db.Set<DocumentChunk>()
            .IgnoreQueryFilters()
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(ct);

        _logger.LogInformation("Re-embed starting for {Total} document chunks (batch {BatchSize}).", ids.Count, batchSize);

        var processed = 0;
        var failed = 0;

        foreach (var batch in Chunk(ids, batchSize))
        {
            ct.ThrowIfCancellationRequested();

            var chunks = await _db.Set<DocumentChunk>()
                .IgnoreQueryFilters()
                .Where(c => batch.Contains(c.Id))
                .Select(c => new { c.Id, c.Content })
                .ToListAsync(ct);

            foreach (var chunk in chunks)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var embedding = await _embeddingService.EmbedAsync(chunk.Content, ct);
                    if (embedding is null || embedding.Length == 0)
                    {
                        failed++;
                        _logger.LogWarning("Empty embedding for chunk {ChunkId}; skipped.", chunk.Id);
                        continue;
                    }

                    var rows = await _db.Set<DocumentChunk>()
                        .IgnoreQueryFilters()
                        .Where(c => c.Id == chunk.Id)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(c => c.Embedding, embedding),
                            ct);

                    if (rows == 1)
                        processed++;
                    else
                        failed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex, "Re-embed failed for chunk {ChunkId}.", chunk.Id);
                }
            }

            _logger.LogInformation("Re-embed progress: {Processed}/{Total} ({Failed} failed).", processed, ids.Count, failed);
        }

        _logger.LogInformation("Re-embed complete: {Processed} updated, {Failed} failed, {Total} total.", processed, failed, ids.Count);
        return processed;
    }

    private static IEnumerable<List<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
            yield return source.Skip(i).Take(size).ToList();
    }
}
