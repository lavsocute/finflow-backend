using FinFlow.Domain.Documents;
using FinFlow.Domain.Interfaces;
using FinFlow.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.UnitTests.Infrastructure.Chat;

public class DocumentChunkModelConfigurationTests
{
    [Fact]
    public void Embedding_IsMappedToPgVectorColumn()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=5434;Database=finflow_db;Username=postgres;Password=postgres123", o => o.UseVector())
            .Options;

        var currentTenant = new TestCurrentTenant
        {
            Id = Guid.NewGuid(),
            MembershipId = Guid.NewGuid()
        };

        using var dbContext = new ApplicationDbContext(options, currentTenant);
        var entityType = dbContext.Model.FindEntityType(typeof(DocumentChunk));
        var embeddingProperty = entityType!.FindProperty(nameof(DocumentChunk.Embedding));

        Assert.NotNull(embeddingProperty);
        Assert.Equal($"vector({ApplicationDbContext.DocumentChunkEmbeddingDimensions})", embeddingProperty!.GetColumnType());
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
