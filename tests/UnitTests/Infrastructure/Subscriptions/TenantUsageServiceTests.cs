using FinFlow.Domain.Entities;
using FinFlow.Domain.TenantUsageSnapshots;
using FinFlow.Infrastructure.Subscriptions;

namespace FinFlow.UnitTests.Infrastructure.Subscriptions;

public sealed class TenantUsageServiceTests
{
    [Fact]
    public async Task GetCurrentUsageAsync_CreatesSnapshotWhenMissing()
    {
        var repository = new InMemoryTenantUsageSnapshotRepository();
        var service = new TenantUsageService(repository);
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var periodStart = new DateOnly(2026, 4, 1);
        var periodEnd = new DateOnly(2026, 4, 30);

        var usage = await service.GetCurrentUsageAsync(tenantId, periodStart, periodEnd, CancellationToken.None);

        Assert.NotNull(usage);
        Assert.Equal(tenantId, usage.IdTenant);
        Assert.Equal(periodStart, usage.PeriodStart);
        Assert.Equal(periodEnd, usage.PeriodEnd);
        Assert.Equal(0, usage.OcrPagesUsed);
        Assert.Single(repository.Items);
    }

    [Fact]
    public async Task RecordOcrUsageAsync_IncrementsExistingSnapshot()
    {
        var repository = new InMemoryTenantUsageSnapshotRepository();
        var service = new TenantUsageService(repository);
        var tenantId = Guid.NewGuid();
        var periodStart = new DateOnly(2026, 4, 1);
        var periodEnd = new DateOnly(2026, 4, 30);

        await service.RecordOcrUsageAsync(tenantId, 2, periodStart, periodEnd, CancellationToken.None);

        var usage = await service.GetCurrentUsageAsync(tenantId, periodStart, periodEnd, CancellationToken.None);

        Assert.Equal(2, usage.OcrPagesUsed);
        Assert.Single(repository.Items);
    }

    private sealed class InMemoryTenantUsageSnapshotRepository : ITenantUsageSnapshotRepository
    {
        private readonly List<TenantUsageSnapshot> _items = [];

        public IReadOnlyCollection<TenantUsageSnapshot> Items => _items;

        public Task<TenantUsageSnapshot?> GetByTenantAndPeriodAsync(
            Guid tenantId,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
        {
            var snapshot = _items.FirstOrDefault(x =>
                x.IdTenant == tenantId &&
                x.PeriodStart == periodStart &&
                x.PeriodEnd == periodEnd);

            return Task.FromResult(snapshot);
        }

        public void Add(TenantUsageSnapshot snapshot) => _items.Add(snapshot);

        public void Update(TenantUsageSnapshot snapshot)
        {
            var index = _items.FindIndex(x => x.Id == snapshot.Id);
            if (index >= 0)
            {
                _items[index] = snapshot;
            }
        }
    }
}
