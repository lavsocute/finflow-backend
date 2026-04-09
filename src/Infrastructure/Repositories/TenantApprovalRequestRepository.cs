using FinFlow.Domain.Entities;
using FinFlow.Domain.TenantApprovals;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class TenantApprovalRequestRepository : ITenantApprovalRequestRepository
{
    private readonly ApplicationDbContext _dbContext;

    public TenantApprovalRequestRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<TenantApprovalRequestSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<TenantApprovalRequest>()
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new TenantApprovalRequestSummary(
                x.Id,
                x.TenantCode,
                x.Name,
                x.CompanyName,
                x.TaxCode,
                x.EmployeeCount,
                x.Currency,
                x.TenancyModel,
                x.RequestedById,
                x.Status,
                x.ExpiresAt,
                x.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<TenantApprovalRequestSummary>> GetPendingAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Set<TenantApprovalRequest>()
            .AsNoTracking()
            .Where(x => x.Status == FinFlow.Domain.Enums.TenantApprovalStatus.Pending)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new TenantApprovalRequestSummary(
                x.Id,
                x.TenantCode,
                x.Name,
                x.CompanyName,
                x.TaxCode,
                x.EmployeeCount,
                x.Currency,
                x.TenancyModel,
                x.RequestedById,
                x.Status,
                x.ExpiresAt,
                x.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<bool> ExistsPendingByTenantCodeAsync(string tenantCode, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<TenantApprovalRequest>()
            .AnyAsync(
                x => x.TenantCode == tenantCode.Trim().ToLowerInvariant()
                  && x.Status == FinFlow.Domain.Enums.TenantApprovalStatus.Pending,
                cancellationToken);

    public async Task<bool> IsTenantCodeBlockedAsync(string tenantCode, DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        var normalizedTenantCode = tenantCode.Trim().ToLowerInvariant();
        var blockedSince = asOfUtc.AddDays(-30);

        return await _dbContext.Set<TenantApprovalRequest>()
            .AnyAsync(
                x => x.TenantCode == normalizedTenantCode
                  && x.Status == FinFlow.Domain.Enums.TenantApprovalStatus.Rejected
                  && x.RejectedAt.HasValue
                  && x.RejectedAt.Value >= blockedSince,
                cancellationToken);
    }

    public async Task<bool> ExistsPendingByRequestedByAsync(Guid requestedById, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<TenantApprovalRequest>()
            .AnyAsync(
                x => x.RequestedById == requestedById
                  && x.Status == FinFlow.Domain.Enums.TenantApprovalStatus.Pending,
                cancellationToken);

    public async Task<TenantApprovalRequest?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<TenantApprovalRequest>()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(TenantApprovalRequest request) => _dbContext.Set<TenantApprovalRequest>().Add(request);

    public void Update(TenantApprovalRequest request) => _dbContext.Set<TenantApprovalRequest>().Update(request);
}
