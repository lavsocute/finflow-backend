using FinFlow.Benchmarks.Benchmarking;
using FinFlow.Domain.Documents;
using FinFlow.Infrastructure;

namespace FinFlow.UnitTests.Infrastructure.Chat;

public class ChatRetrievalSeedFactoryTests
{
    [Fact]
    public void Create_ReturnsDeterministicSeedWithExpectedDistribution()
    {
        var options = new ChatRetrievalBenchmarkOptions
        {
            TenantCount = 2,
            DepartmentsPerTenant = 2,
            OwnersPerDepartment = 2,
            ChunksPerOwner = 3,
            EmbeddingDimensions = ApplicationDbContext.DocumentChunkEmbeddingDimensions
        };

        var seed = ChatRetrievalSeedFactory.Create(options);

        Assert.Equal(24, seed.Chunks.Count);
        Assert.Equal(2, seed.TenantContexts.Count);
        Assert.All(seed.Chunks, chunk => Assert.Equal(ApplicationDbContext.DocumentChunkEmbeddingDimensions, chunk.Embedding.Length));
        Assert.All(seed.TenantContexts, context =>
        {
            Assert.Equal(2, context.DepartmentIds.Count);
            Assert.Equal(4, context.OwnerIds.Count);
            Assert.Equal(ApplicationDbContext.DocumentChunkEmbeddingDimensions, context.QueryEmbedding.Length);
        });
    }

    [Fact]
    public void Create_ProducesStableTenantAndOwnerIdsAcrossRuns()
    {
        var options = new ChatRetrievalBenchmarkOptions
        {
            TenantCount = 1,
            DepartmentsPerTenant = 2,
            OwnersPerDepartment = 2,
            ChunksPerOwner = 1
        };

        var first = ChatRetrievalSeedFactory.Create(options);
        var second = ChatRetrievalSeedFactory.Create(options);

        Assert.Equal(first.TenantContexts[0].TenantId, second.TenantContexts[0].TenantId);
        Assert.Equal(first.TenantContexts[0].DepartmentIds, second.TenantContexts[0].DepartmentIds);
        Assert.Equal(first.TenantContexts[0].OwnerIds, second.TenantContexts[0].OwnerIds);
        Assert.Equal(first.Chunks.Select(x => x.Id), second.Chunks.Select(x => x.Id));
    }

    [Fact]
    public void Create_AssignsOnlyConfiguredChunkTypes()
    {
        var options = new ChatRetrievalBenchmarkOptions
        {
            ChunkTypes =
            [
                DocumentChunkType.Expense,
                DocumentChunkType.Receipt
            ],
            ChunksPerOwner = 5
        };

        var seed = ChatRetrievalSeedFactory.Create(options);

        Assert.All(seed.Chunks, chunk => Assert.Contains(chunk.Type, options.ChunkTypes));
    }
}
