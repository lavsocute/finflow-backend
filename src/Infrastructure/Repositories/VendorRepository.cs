using FinFlow.Domain.Entities;
using FinFlow.Domain.Vendors;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class VendorRepository : IVendorRepository
{
    private readonly ApplicationDbContext _dbContext;

    public VendorRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<VendorSummary?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Vendor>()
            .AsNoTracking()
            .Where(x => x.Id == id && x.IdTenant == tenantId)
            .Select(x => new VendorSummary(
                x.Id,
                x.IdTenant,
                x.TaxCode,
                x.Name,
                x.IsVerified,
                x.VerifiedByMembershipId,
                x.VerifiedAt,
                x.CreatedAt,
                x.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<VendorSummary?> GetByTaxCodeAsync(string taxCode, Guid tenantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Vendor>()
            .AsNoTracking()
            .Where(x => x.TaxCode == taxCode.Trim() && x.IdTenant == tenantId)
            .Select(x => new VendorSummary(
                x.Id,
                x.IdTenant,
                x.TaxCode,
                x.Name,
                x.IsVerified,
                x.VerifiedByMembershipId,
                x.VerifiedAt,
                x.CreatedAt,
                x.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> ExistsByTaxCodeAsync(string taxCode, Guid tenantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Vendor>()
            .AnyAsync(x => x.TaxCode == taxCode.Trim() && x.IdTenant == tenantId, cancellationToken);

    public async Task<IReadOnlyList<VendorSummary>> GetAllAsync(Guid tenantId, bool? isVerified = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Vendor>()
            .AsNoTracking()
            .Where(x => x.IdTenant == tenantId);

        if (isVerified.HasValue)
            query = query.Where(x => x.IsVerified == isVerified.Value);

        return await query
            .OrderBy(x => x.Name)
            .Select(x => new VendorSummary(
                x.Id,
                x.IdTenant,
                x.TaxCode,
                x.Name,
                x.IsVerified,
                x.VerifiedByMembershipId,
                x.VerifiedAt,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<Vendor?> GetEntityByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Vendor>()
            .FirstOrDefaultAsync(x => x.Id == id && x.IdTenant == tenantId, cancellationToken);

    public async Task<Vendor?> GetEntityByTaxCodeAsync(string taxCode, Guid tenantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Vendor>()
            .FirstOrDefaultAsync(x => x.TaxCode == taxCode.Trim() && x.IdTenant == tenantId, cancellationToken);

    public void Add(Vendor vendor) => _dbContext.Set<Vendor>().Add(vendor);
    public void Update(Vendor vendor) => _dbContext.Set<Vendor>().Update(vendor);
}