using FinFlow.Domain.Entities;
using FinFlow.Domain.Interfaces;
using FinFlow.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FinFlow.UnitTests.Infrastructure;

public sealed class MemberUsageSnapshotConfigurationTests
{
    [Fact]
    public void Id_PreservesLegacyColumnName()
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
        var entityType = dbContext.Model.FindEntityType(typeof(MemberUsageSnapshot));
        var idProperty = entityType!.FindProperty(nameof(MemberUsageSnapshot.Id));
        var table = StoreObjectIdentifier.Table("member_usage_snapshot", null);

        Assert.NotNull(idProperty);
        Assert.Equal("Id", idProperty!.GetColumnName(table));
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
