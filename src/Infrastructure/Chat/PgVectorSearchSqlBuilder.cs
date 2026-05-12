using FinFlow.Domain.Documents;
using Npgsql;
using NpgsqlTypes;
using Pgvector;

namespace FinFlow.Infrastructure.Chat;

internal static class PgVectorSearchSqlBuilder
{
    public static PgVectorSearchCommand Build(
        Guid tenantId,
        Guid? departmentId,
        Guid? ownerId,
        IReadOnlyCollection<DocumentChunkType>? allowedTypes,
        float[] queryEmbedding,
        int topK)
    {
        if (queryEmbedding.Length == 0)
            throw new InvalidOperationException("Query embedding cannot be empty.");

        var sql = """
            SELECT c."Id" AS "Value"
            FROM document_chunks AS c
            WHERE c."IdTenant" = @tenantId
              AND (@departmentId IS NULL OR c."DepartmentId" = @departmentId)
              AND (@ownerId IS NULL OR c."OwnerMembershipId" = @ownerId)
              AND (@allowedTypes IS NULL OR c."Type" = ANY(@allowedTypes))
              AND vector_dims(c."Embedding") = @queryDimensions
            ORDER BY c."Embedding" <=> @queryEmbedding
            LIMIT @topK
            """;

        var parameters = new List<NpgsqlParameter>
        {
            new("tenantId", NpgsqlDbType.Uuid) { Value = tenantId },
            new("departmentId", NpgsqlDbType.Uuid) { Value = departmentId.HasValue ? departmentId.Value : DBNull.Value },
            new("ownerId", NpgsqlDbType.Uuid) { Value = ownerId.HasValue ? ownerId.Value : DBNull.Value },
            new("allowedTypes", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = allowedTypes is { Count: > 0 }
                    ? allowedTypes.Select(x => x.ToString()).ToArray()
                    : DBNull.Value
            },
            new("queryDimensions", NpgsqlDbType.Integer) { Value = queryEmbedding.Length },
            new("queryEmbedding", new Vector(queryEmbedding)),
            new("topK", NpgsqlDbType.Integer) { Value = topK }
        };

        return new PgVectorSearchCommand(sql, parameters);
    }
}

internal sealed record PgVectorSearchCommand(string Sql, IReadOnlyList<NpgsqlParameter> Parameters);
