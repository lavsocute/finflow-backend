using FinFlow.Domain.Entities;
using FinFlow.Domain.TenantSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class TenantSubscriptionRepository : ITenantSubscriptionRepository
{
    private readonly ApplicationDbContext _dbContext;

    public TenantSubscriptionRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<TenantSubscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<TenantSubscription>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdTenant == tenantId, cancellationToken);

    public void Add(TenantSubscription tenantSubscription) => _dbContext.Set<TenantSubscription>().Add(tenantSubscription);

    public void Update(TenantSubscription tenantSubscription) => _dbContext.Set<TenantSubscription>().Update(tenantSubscription);
}
