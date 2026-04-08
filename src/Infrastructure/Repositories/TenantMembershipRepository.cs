using FinFlow.Domain.Entities;
using FinFlow.Domain.TenantMemberships;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class TenantMembershipRepository : ITenantMembershipRepository
{
    private readonly ApplicationDbContext _dbContext;

    public TenantMembershipRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<TenantMembershipSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<TenantMembership>()
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new TenantMembershipSummary(m.Id, m.AccountId, m.IdTenant, m.Role, m.IsActive, m.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<TenantMembershipSummary?> GetActiveByAccountAndTenantAsync(Guid accountId, Guid idTenant, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<TenantMembership>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(m => m.AccountId == accountId && m.IdTenant == idTenant && m.IsActive)
            .Select(m => new TenantMembershipSummary(m.Id, m.AccountId, m.IdTenant, m.Role, m.IsActive, m.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<TenantMembershipSummary>> GetActiveByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<TenantMembership>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(m => m.AccountId == accountId && m.IsActive)
            .Select(m => new TenantMembershipSummary(m.Id, m.AccountId, m.IdTenant, m.Role, m.IsActive, m.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TenantMembershipSummary>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<TenantMembership>()
            .AsNoTracking()
            .Where(m => m.AccountId == accountId)
            .Select(m => new TenantMembershipSummary(m.Id, m.AccountId, m.IdTenant, m.Role, m.IsActive, m.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TenantMembershipSummary>> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<TenantMembership>()
            .AsNoTracking()
            .Where(m => m.IdTenant == idTenant)
            .Select(m => new TenantMembershipSummary(m.Id, m.AccountId, m.IdTenant, m.Role, m.IsActive, m.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<bool> ExistsAsync(Guid accountId, Guid idTenant, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<TenantMembership>()
            .IgnoreQueryFilters()
            .AnyAsync(m => m.AccountId == accountId && m.IdTenant == idTenant, cancellationToken);

    public async Task<TenantMembership?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<TenantMembership>().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public void Add(TenantMembership membership) => _dbContext.Set<TenantMembership>().Add(membership);
    public void Update(TenantMembership membership) => _dbContext.Set<TenantMembership>().Update(membership);
}
