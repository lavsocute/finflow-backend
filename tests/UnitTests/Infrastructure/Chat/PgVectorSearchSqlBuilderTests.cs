using FinFlow.Domain.Documents;
using FinFlow.Infrastructure.Chat;
using NpgsqlTypes;

namespace FinFlow.UnitTests.Infrastructure.Chat;

public class PgVectorSearchSqlBuilderTests
{
    [Fact]
    public void Build_IncludesPgVectorCosineOrderingAndPolicyFilters()
    {
        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        var command = PgVectorSearchSqlBuilder.Build(
            tenantId,
            departmentId,
            ownerId,
            new[] { DocumentChunkType.Expense, DocumentChunkType.Receipt },
            new[] { 0.1f, 0.2f, 0.3f },
            topK: 7);

        Assert.Contains("ORDER BY c.\"Embedding\" <=> @queryEmbedding", command.Sql);
        Assert.Contains("c.\"IdTenant\" = @tenantId", command.Sql);
        Assert.Contains("(@departmentId IS NULL OR c.\"DepartmentId\" = @departmentId)", command.Sql);
        Assert.Contains("(@ownerId IS NULL OR c.\"OwnerMembershipId\" = @ownerId)", command.Sql);
        Assert.Contains("(@allowedTypes IS NULL OR c.\"Type\" = ANY(@allowedTypes))", command.Sql);
        Assert.Contains("vector_dims(c.\"Embedding\") = @queryDimensions", command.Sql);
        Assert.Contains("LIMIT @topK", command.Sql);
    }

    [Fact]
    public void Build_CreatesExpectedParameterTypes()
    {
        var command = PgVectorSearchSqlBuilder.Build(
            Guid.NewGuid(),
            departmentId: null,
            ownerId: null,
            allowedTypes: new[] { DocumentChunkType.Budget },
            queryEmbedding: new[] { 1f, 2f, 3f },
            topK: 5);

        var parameters = command.Parameters.ToDictionary(p => p.ParameterName);

        Assert.Equal(NpgsqlDbType.Uuid, parameters["tenantId"].NpgsqlDbType);
        Assert.Equal(NpgsqlDbType.Uuid, parameters["departmentId"].NpgsqlDbType);
        Assert.Equal(DBNull.Value, parameters["departmentId"].Value);
        Assert.Equal(NpgsqlDbType.Uuid, parameters["ownerId"].NpgsqlDbType);
        Assert.Equal(DBNull.Value, parameters["ownerId"].Value);
        Assert.Equal(NpgsqlDbType.Array | NpgsqlDbType.Text, parameters["allowedTypes"].NpgsqlDbType);
        Assert.Equal(new[] { nameof(DocumentChunkType.Budget) }, parameters["allowedTypes"].Value);
        Assert.Equal(NpgsqlDbType.Integer, parameters["queryDimensions"].NpgsqlDbType);
        Assert.Equal(3, parameters["queryDimensions"].Value);
        Assert.Equal(NpgsqlDbType.Integer, parameters["topK"].NpgsqlDbType);
        Assert.Equal(5, parameters["topK"].Value);
    }
}
