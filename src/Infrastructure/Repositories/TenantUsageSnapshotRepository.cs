using FinFlow.Domain.Entities;
using FinFlow.Domain.TenantUsageSnapshots;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class TenantUsageSnapshotRepository : ITenantUsageSnapshotRepository
{
    private readonly ApplicationDbContext _dbContext;

    public TenantUsageSnapshotRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<TenantUsageSnapshot?> GetByTenantAndPeriodAsync(
        Guid tenantId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        var trackedSnapshot = _dbContext.Set<TenantUsageSnapshot>().Local
            .FirstOrDefault(
                x => x.IdTenant == tenantId &&
                     x.PeriodStart == periodStart &&
                     x.PeriodEnd == periodEnd);

        if (trackedSnapshot is not null)
            return trackedSnapshot;

        return await _dbContext.Set<TenantUsageSnapshot>()
            .FirstOrDefaultAsync(
                x => x.IdTenant == tenantId &&
                     x.PeriodStart == periodStart &&
                     x.PeriodEnd == periodEnd,
                cancellationToken);
    }

    public void Add(TenantUsageSnapshot snapshot) => _dbContext.Set<TenantUsageSnapshot>().Add(snapshot);

    public void Update(TenantUsageSnapshot snapshot) => _dbContext.Set<TenantUsageSnapshot>().Update(snapshot);
}
