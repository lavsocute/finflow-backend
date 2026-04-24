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
            .FirstOrDefaultAsync(x => x.Id == id && x.IdTenant == tenantId && x.IsActive, cancellationToken);

    public async Task<bool> ExistsAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<ReviewedDocument>()
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Id == id && x.IdTenant == tenantId && x.IsActive, cancellationToken);

    public async Task<IReadOnlyList<ReviewedDocument>> GetReadyForApprovalAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<ReviewedDocument>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.IdTenant == tenantId && x.Status == ReviewedDocumentStatus.ReadyForApproval && x.IsActive)
            .OrderByDescending(x => x.SubmittedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReviewedDocument>> GetOwnedSubmittedAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<ReviewedDocument>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.IdTenant == tenantId && x.MembershipId == membershipId && x.IsActive)
            .OrderByDescending(x => x.SubmittedAt)
            .ToListAsync(cancellationToken);
}
