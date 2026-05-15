using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Documents;
using FinFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinFlow.Infrastructure.Chat;

public class PgVectorStore : IVectorStore
{
    private readonly ApplicationDbContext _dbContext;

    public PgVectorStore(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task UpsertChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct = default)
    {
        var chunkList = chunks.ToList();
        if (chunkList.Count == 0) return;

        var idsToCheck = chunkList.Select(c => c.Id).ToList();
        var existingIdsList = await _dbContext.DocumentChunks
            .Where(c => idsToCheck.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);
        var existingIds = existingIdsList.ToHashSet();

        foreach (var chunk in chunkList)
        {
            if (!existingIds.Contains(chunk.Id))
            {
                _dbContext.DocumentChunks.Add(chunk);
            }
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DocumentChunk>> SearchAsync(
        float[] queryEmbedding,
        Guid tenantId,
        Guid? departmentId,
        Guid? ownerId,
        IReadOnlyCollection<DocumentChunkType>? allowedTypes = null,
        int topK = 20,
        CancellationToken ct = default)
    {
        ValidateSearchRequest(queryEmbedding, tenantId, allowedTypes, topK);

        var command = PgVectorSearchSqlBuilder.Build(
            tenantId,
            departmentId,
            ownerId,
            allowedTypes,
            queryEmbedding,
            topK);

        var orderedIds = await _dbContext.Database
            .SqlQueryRaw<Guid>(command.Sql, command.Parameters.Cast<object>().ToArray())
            .ToListAsync(ct);

        if (orderedIds.Count == 0)
            return [];

        var chunks = await _dbContext.DocumentChunks
            .AsNoTracking()
            .Where(c => orderedIds.Contains(c.Id))
            .ToListAsync(ct);

        var orderLookup = orderedIds
            .Select((id, index) => new { id, index })
            .ToDictionary(x => x.id, x => x.index);

        return chunks
            .OrderBy(c => orderLookup[c.Id])
            .ToList();
    }

    public async Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken ct = default)
    {
        var chunks = await _dbContext.DocumentChunks
            .Where(c => c.DocumentId == documentId)
            .ToListAsync(ct);

        _dbContext.DocumentChunks.RemoveRange(chunks);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task ReplaceDocumentChunksAsync(Guid documentId, IEnumerable<DocumentChunk> newChunks, CancellationToken ct = default)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

            var existing = await _dbContext.DocumentChunks
                .Where(c => c.DocumentId == documentId)
                .ToListAsync(ct);

            _dbContext.DocumentChunks.RemoveRange(existing);

            var chunkList = newChunks.ToList();
            if (chunkList.Count > 0)
            {
                _dbContext.DocumentChunks.AddRange(chunkList);
            }

            await _dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
    }

    private static void ValidateSearchRequest(
        float[] queryEmbedding,
        Guid tenantId,
        IReadOnlyCollection<DocumentChunkType>? allowedTypes,
        int topK)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        ArgumentNullException.ThrowIfNull(allowedTypes);

        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenantId is required.", nameof(tenantId));

        if (allowedTypes.Count == 0)
            throw new ArgumentException("allowedTypes cannot be empty when provided.", nameof(allowedTypes));

        if (topK <= 0)
            throw new ArgumentOutOfRangeException(nameof(topK), topK, "topK must be greater than zero.");
    }
}
