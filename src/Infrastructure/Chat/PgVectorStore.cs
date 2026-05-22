using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Documents;
using FinFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

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

    public async Task<IReadOnlyList<DocumentChunk>> KeywordSearchAsync(
        string query,
        Guid tenantId,
        Guid? departmentId,
        Guid? ownerId,
        IReadOnlyCollection<DocumentChunkType>? allowedTypes = null,
        int topK = 20,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenantId is required.", nameof(tenantId));
        if (allowedTypes is not { Count: > 0 })
            throw new ArgumentException("allowedTypes cannot be empty when provided.", nameof(allowedTypes));
        if (string.IsNullOrWhiteSpace(query))
            return [];
        if (topK <= 0)
            return [];

        // Defense-in-depth: plainto_tsquery accepts boolean operators (|, *, (), &, &&, ||, !, NOT, AND, OR)
        // which could manipulate tsquery behavior. NpgsqlParameter binding prevents SQL injection,
        // but these operators can still cause unexpected query semantics or parse errors.
        // Reject queries containing these characters to ensure predictable search behavior.
        if (ContainsTsQueryBooleanOperators(query))
            throw new ArgumentException("Query contains disallowed characters for text search.", nameof(query));

        var allowedTypeStrings = allowedTypes is { Count: > 0 }
            ? allowedTypes.Select(x => x.ToString()).ToArray()
            : null;

        var sql = """
            SELECT c."Id" AS "Value"
            FROM document_chunks AS c
            WHERE c."IdTenant" = @tenantId
              AND (@departmentId IS NULL OR c."DepartmentId" = @departmentId)
              AND (@ownerId IS NULL OR c."OwnerMembershipId" = @ownerId)
              AND (@allowedTypes IS NULL OR c."Type" = ANY(@allowedTypes))
              AND to_tsvector('simple', c."Content") @@ plainto_tsquery('simple', @query)
            ORDER BY ts_rank_cd(to_tsvector('simple', c."Content"), plainto_tsquery('simple', @query)) DESC
            LIMIT @topK
            """;

        var parameters = new List<NpgsqlParameter>
        {
            new("tenantId", NpgsqlDbType.Uuid) { Value = tenantId },
            new("departmentId", NpgsqlDbType.Uuid) { Value = departmentId.HasValue ? departmentId.Value : DBNull.Value },
            new("ownerId", NpgsqlDbType.Uuid) { Value = ownerId.HasValue ? ownerId.Value : DBNull.Value },
            new("allowedTypes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = (object?)allowedTypeStrings ?? DBNull.Value },
            new("query", NpgsqlDbType.Text) { Value = query },
            new("topK", NpgsqlDbType.Integer) { Value = topK }
        };

        var orderedIds = await _dbContext.Database
            .SqlQueryRaw<Guid>(sql, parameters.Cast<object>().ToArray())
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

        return chunks.OrderBy(c => orderLookup[c.Id]).ToList();
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
        int topK,
        string? query = null)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        ArgumentNullException.ThrowIfNull(allowedTypes);

        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenantId is required.", nameof(tenantId));

        if (allowedTypes.Count == 0)
            throw new ArgumentException("allowedTypes cannot be empty when provided.", nameof(allowedTypes));

        if (topK <= 0)
            throw new ArgumentOutOfRangeException(nameof(topK), topK, "topK must be greater than zero.");

        // FIX #4: Add upper bound to prevent DoS via excessive memory allocation
        if (topK > 1000)
            throw new ArgumentOutOfRangeException(nameof(topK), topK, "topK must not exceed 1000.");

        // FIX #4: Add query length limit to prevent DoS via oversized query string
        if (!string.IsNullOrWhiteSpace(query) && query.Length > 10000)
            throw new ArgumentException("Query must not exceed 10000 characters.", nameof(query));
    }

    // Defense-in-depth: rejects tsquery boolean operators that could manipulate search behavior
    private static bool ContainsTsQueryBooleanOperators(string query)
    {
        return query.Contains('|') || query.Contains('*') || query.Contains('(') ||
               query.Contains(')') || query.Contains('&') || query.Contains('!') ||
               query.Contains("NOT", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("AND", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("OR", StringComparison.OrdinalIgnoreCase);
    }
}
