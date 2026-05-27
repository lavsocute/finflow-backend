using FinFlow.Application.Vendors.Services;
using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Vendors;

internal sealed class VendorWorkspaceReadService : IVendorWorkspaceReadService
{
    private const int RecentDocumentLimit = 5;
    private readonly ApplicationDbContext _dbContext;

    public VendorWorkspaceReadService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetLinkedDocumentCountsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> vendorIds,
        CancellationToken cancellationToken = default)
    {
        if (vendorIds.Count == 0)
            return new Dictionary<Guid, int>();

        var documentVendorIds = await _dbContext.ReviewedDocuments
            .AsNoTracking()
            .Where(document =>
                document.IdTenant == tenantId &&
                document.IdVendor.HasValue &&
                vendorIds.Contains(document.IdVendor.Value))
            .Select(document => new { VendorId = document.IdVendor!.Value })
            .ToListAsync(cancellationToken);

        return documentVendorIds
            .GroupBy(document => document.VendorId)
            .ToDictionary(group => group.Key, group => group.Count());
    }

    public async Task<VendorDetailReadModel?> GetDetailAsync(
        Guid tenantId,
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.Set<Vendor>()
            .AsNoTracking()
            .AnyAsync(vendor => vendor.IdTenant == tenantId && vendor.Id == vendorId, cancellationToken);

        if (!exists)
            return null;

        var query = _dbContext.ReviewedDocuments
            .AsNoTracking()
            .Where(document => document.IdTenant == tenantId && document.IdVendor == vendorId);

        var count = await query.CountAsync(cancellationToken);
        var documentRows = await query
            .OrderByDescending(document => document.SubmittedAt)
            .Take(RecentDocumentLimit)
            .Select(document => new
            {
                document.Id,
                document.Reference,
                document.Category,
                document.Status,
                document.TotalAmount,
                document.CurrencyCode,
                document.DocumentDate
            })
            .ToListAsync(cancellationToken);

        var recentDocuments = documentRows
            .Select(document => new VendorLinkedDocumentReadModel(
                document.Id,
                document.Reference,
                document.Category,
                document.Status.ToString(),
                document.TotalAmount,
                document.CurrencyCode,
                document.DocumentDate))
            .ToList();

        return new VendorDetailReadModel(vendorId, count, recentDocuments);
    }
}
