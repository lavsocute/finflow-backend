using FinFlow.Domain.Entities;
using FinFlow.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class TenantRepository : ITenantRepository
{
    private readonly ApplicationDbContext _dbContext;

    public TenantRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<TenantSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Tenant>()
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TenantSummary(t.Id, t.Name, t.TenantCode, t.TenancyModel, t.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<TenantSummary?> GetByCodeAsync(string tenantCode, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Tenant>()
            .AsNoTracking()
            .Where(t => t.TenantCode == tenantCode.Trim().ToLowerInvariant())
            .Select(t => new TenantSummary(t.Id, t.Name, t.TenantCode, t.TenancyModel, t.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> ExistsByCodeAsync(string tenantCode, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Tenant>()
            .AnyAsync(t => t.TenantCode == tenantCode.Trim().ToLowerInvariant(), cancellationToken);

    public async Task<IReadOnlyList<TenantSummary>> GetAllActiveAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Tenant>()
            .AsNoTracking()
            .Where(t => t.IsActive)
            .Select(t => new TenantSummary(t.Id, t.Name, t.TenantCode, t.TenancyModel, t.IsActive))
            .ToListAsync(cancellationToken);

    public void Add(Tenant tenant) => _dbContext.Set<Tenant>().Add(tenant);
    public void Update(Tenant tenant) => _dbContext.Set<Tenant>().Update(tenant);
    public void Remove(Tenant tenant) => _dbContext.Set<Tenant>().Remove(tenant);
}
