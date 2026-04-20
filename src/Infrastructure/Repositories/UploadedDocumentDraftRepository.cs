using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class UploadedDocumentDraftRepository : IUploadedDocumentDraftRepository
{
    private readonly ApplicationDbContext _dbContext;

    public UploadedDocumentDraftRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public void Add(UploadedDocumentDraft draft) => _dbContext.Set<UploadedDocumentDraft>().Add(draft);

    public void Update(UploadedDocumentDraft draft) => _dbContext.Set<UploadedDocumentDraft>().Update(draft);

    public async Task<UploadedDocumentDraft?> GetByIdAsync(Guid id, Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<UploadedDocumentDraft>()
            .IgnoreQueryFilters()
            .Include(x => x.LineItems)
            .FirstOrDefaultAsync(
                x => x.Id == id && x.IdTenant == tenantId && x.MembershipId == membershipId && x.IsActive,
                cancellationToken);

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<UploadedDocumentDraft>()
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Id == id && x.IsActive, cancellationToken);

    public async Task<IReadOnlyList<UploadedDocumentDraft>> GetOwnedActiveAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<UploadedDocumentDraft>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.IdTenant == tenantId && x.MembershipId == membershipId && x.IsActive)
            .OrderByDescending(x => x.UploadedAt)
            .ToListAsync(cancellationToken);
}
