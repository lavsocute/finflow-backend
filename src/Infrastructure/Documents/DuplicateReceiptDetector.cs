using FinFlow.Application.Documents.Duplicates;
using FinFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Documents;

internal sealed class DuplicateReceiptDetector : IDuplicateReceiptDetector
{
    private readonly ApplicationDbContext _dbContext;

    public DuplicateReceiptDetector(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<Guid>> FindMatchesAsync(
        Guid tenantId,
        string dedupHash,
        Guid currentDocumentId,
        int slidingWindowDays,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dedupHash))
            return [];

        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, slidingWindowDays));

        // Match same hash, same tenant, not the current doc, not rejected /
        // withdrawn (those are no longer authoritative receipts), within window.
        var matches = await _dbContext.ReviewedDocuments
            .AsNoTracking()
            .Where(d => d.IdTenant == tenantId
                && d.DedupHash == dedupHash
                && d.Id != currentDocumentId
                && d.CreatedAt >= cutoff
                && d.Status != ReviewedDocumentStatus.Rejected
                && d.Status != ReviewedDocumentStatus.Draft)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        return matches;
    }
}
