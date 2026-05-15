using FinFlow.Domain.Documents;
using FinFlow.Domain.Interfaces;
using FinFlow.Infrastructure;
using FinFlow.Infrastructure.Chat;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.UnitTests.Infrastructure.Chat;

public class PgVectorStoreSecurityTests
{
    [Fact]
    public async Task SearchAsync_Should_Reject_EmptyTenantId()
    {
        await using var dbContext = CreateDbContext();
        var sut = new PgVectorStore(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.SearchAsync(
                queryEmbedding: [0.1f, 0.2f],
                tenantId: Guid.Empty,
                departmentId: null,
                ownerId: null,
                allowedTypes: [DocumentChunkType.Expense],
                topK: 5,
                ct: CancellationToken.None));

        Assert.Contains("tenantId", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_Should_Reject_EmptyAllowedTypes_ToAvoid_Unscoped_Search()
    {
        await using var dbContext = CreateDbContext();
        var sut = new PgVectorStore(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.SearchAsync(
                queryEmbedding: [0.1f, 0.2f],
                tenantId: Guid.NewGuid(),
                departmentId: null,
                ownerId: null,
                allowedTypes: Array.Empty<DocumentChunkType>(),
                topK: 5,
                ct: CancellationToken.None));

        Assert.Contains("allowedTypes", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_Should_Reject_NullAllowedTypes_ToAvoid_Unscoped_Search()
    {
        await using var dbContext = CreateDbContext();
        var sut = new PgVectorStore(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.SearchAsync(
                queryEmbedding: [0.1f, 0.2f],
                tenantId: Guid.NewGuid(),
                departmentId: null,
                ownerId: null,
                allowedTypes: null,
                topK: 5,
                ct: CancellationToken.None));

        Assert.Equal("allowedTypes", ex.ParamName);
    }

    [Fact]
    public async Task SearchAsync_Should_Reject_NullQueryEmbedding()
    {
        await using var dbContext = CreateDbContext();
        var sut = new PgVectorStore(dbContext);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.SearchAsync(
                queryEmbedding: null!,
                tenantId: Guid.NewGuid(),
                departmentId: null,
                ownerId: null,
                allowedTypes: [DocumentChunkType.Expense],
                topK: 5,
                ct: CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_Should_Reject_EmptyQueryEmbedding()
    {
        await using var dbContext = CreateDbContext();
        var sut = new PgVectorStore(dbContext);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.SearchAsync(
                queryEmbedding: Array.Empty<float>(),
                tenantId: Guid.NewGuid(),
                departmentId: null,
                ownerId: null,
                allowedTypes: [DocumentChunkType.Expense],
                topK: 5,
                ct: CancellationToken.None));

        Assert.Contains("Query embedding cannot be empty", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_Should_Reject_NonPositive_TopK()
    {
        await using var dbContext = CreateDbContext();
        var sut = new PgVectorStore(dbContext);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            sut.SearchAsync(
                queryEmbedding: [0.1f, 0.2f],
                tenantId: Guid.NewGuid(),
                departmentId: null,
                ownerId: null,
                allowedTypes: [DocumentChunkType.Expense],
                topK: 0,
                ct: CancellationToken.None));

        Assert.Equal("topK", ex.ParamName);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options, new TestCurrentTenant
        {
            Id = Guid.NewGuid(),
            MembershipId = Guid.NewGuid()
        });
    }

    private sealed class TestCurrentTenant : ICurrentTenant
    {
        public Guid? Id { get; set; }
        public Guid? MembershipId { get; set; }
        public bool IsSuperAdmin { get; set; }
        public bool IsAvailable => Id.HasValue;

        public IDisposable BeginScope(Guid? tenantId, Guid? membershipId = null, bool isSuperAdmin = false)
            => NoOpDisposable.Instance;
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new();
        public void Dispose() { }
    }
}
