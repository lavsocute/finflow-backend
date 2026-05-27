using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class ReviewedDocumentRepository : IReviewedDocumentRepository
{
    private readonly ApplicationDbContext _dbContext;

    public ReviewedDocumentRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public void Add(ReviewedDocument document) => _dbContext.Set<ReviewedDocument>().Add(document);

    public void Update(ReviewedDocument document) => _dbContext.Set<ReviewedDocument>().Update(document);

    public async Task<ReviewedDocument?> GetByIdForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<ReviewedDocument>()
            .IgnoreQueryFilters()
            .Include(x => x.TaxLines)
            .FirstOrDefaultAsync(x => x.Id == id && x.IdTenant == tenantId && x.IsActive, cancellationToken);

    public async Task<ReviewedDocument?> GetOwnedByIdAsync(Guid id, Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<ReviewedDocument>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.TaxLines)
            .FirstOrDefaultAsync(
                x => x.Id == id && x.IdTenant == tenantId && x.MembershipId == membershipId && x.IsActive,
                cancellationToken);

    public async Task<bool> ExistsAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<ReviewedDocument>()
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Id == id && x.IdTenant == tenantId && x.IsActive, cancellationToken);

    public async Task<IReadOnlyList<ReviewedDocument>> GetAllActiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<ReviewedDocument>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.IdTenant == tenantId && x.IsActive)
            .OrderByDescending(x => x.SubmittedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReviewedDocument>> GetReadyForApprovalAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<ReviewedDocument>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.IdTenant == tenantId && x.Status == ReviewedDocumentStatus.ReadyForApproval && x.IsActive)
            .OrderByDescending(x => x.SubmittedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReviewedDocument>> GetReadyForApprovalByDepartmentAsync(Guid tenantId, Guid departmentId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<ReviewedDocument>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.IdTenant == tenantId && x.IdDepartment == departmentId && x.Status == ReviewedDocumentStatus.ReadyForApproval && x.IsActive)
            .OrderByDescending(x => x.SubmittedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReviewedDocument>> GetOwnedReadyForApprovalAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<ReviewedDocument>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.IdTenant == tenantId && x.MembershipId == membershipId && x.Status == ReviewedDocumentStatus.ReadyForApproval && x.IsActive)
            .OrderByDescending(x => x.SubmittedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReviewedDocument>> GetOwnedSubmittedAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<ReviewedDocument>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.IdTenant == tenantId && x.MembershipId == membershipId && x.IsActive)
            .OrderByDescending(x => x.SubmittedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReviewedDocument>> GetByStatusAsync(Guid tenantId, ApprovalStatusFilter status, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<ReviewedDocument>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.IdTenant == tenantId && x.IsActive);

        query = status switch
        {
            ApprovalStatusFilter.Pending => query.Where(x => x.Status == ReviewedDocumentStatus.ReadyForApproval),
            ApprovalStatusFilter.Approved => query.Where(x => x.Status == ReviewedDocumentStatus.Approved),
            ApprovalStatusFilter.Rejected => query.Where(x => x.Status == ReviewedDocumentStatus.Rejected),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLowerInvariant();
            query = query.Where(x =>
                x.VendorName.ToLower().Contains(searchLower) ||
                x.Reference.ToLower().Contains(searchLower) ||
                x.Category.ToLower().Contains(searchLower));
        }

        return await query
            .OrderByDescending(x => x.SubmittedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountByStatusAsync(Guid tenantId, ApprovalStatusFilter status, string? search, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<ReviewedDocument>()
            .IgnoreQueryFilters()
            .Where(x => x.IdTenant == tenantId && x.IsActive);

        query = status switch
        {
            ApprovalStatusFilter.Pending => query.Where(x => x.Status == ReviewedDocumentStatus.ReadyForApproval),
            ApprovalStatusFilter.Approved => query.Where(x => x.Status == ReviewedDocumentStatus.Approved),
            ApprovalStatusFilter.Rejected => query.Where(x => x.Status == ReviewedDocumentStatus.Rejected),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLowerInvariant();
            query = query.Where(x =>
                x.VendorName.ToLower().Contains(searchLower) ||
                x.Reference.ToLower().Contains(searchLower) ||
                x.Category.ToLower().Contains(searchLower));
        }

        return await query.CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewedDocument>> GetByIdsAsync(
        IReadOnlyList<Guid> ids,
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.Set<ReviewedDocument>()
            .AsNoTracking()
            .Include(d => d.TaxLines)
            .Where(d => d.IdTenant == tenantId && ids.Contains(d.Id))
            .ToListAsync(cancellationToken);
}
