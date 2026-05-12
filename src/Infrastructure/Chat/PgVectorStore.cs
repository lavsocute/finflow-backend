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
        foreach (var chunk in chunks)
        {
            var exists = await _dbContext.DocumentChunks
                .AnyAsync(c => c.Id == chunk.Id, ct);

            if (!exists)
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
}
