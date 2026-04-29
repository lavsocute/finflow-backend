using FinFlow.Domain.Entities;

namespace FinFlow.Domain.Vendors;

public record VendorSummary(
    Guid Id,
    Guid IdTenant,
    string TaxCode,
    string Name,
    bool IsVerified,
    Guid? VerifiedByMembershipId,
    DateTime? VerifiedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public interface IVendorRepository
{
    Task<VendorSummary?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    Task<VendorSummary?> GetByTaxCodeAsync(string taxCode, Guid tenantId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByTaxCodeAsync(string taxCode, Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VendorSummary>> GetAllAsync(Guid tenantId, bool? isVerified = null, CancellationToken cancellationToken = default);
    Task<Vendor?> GetEntityByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    Task<Vendor?> GetEntityByTaxCodeAsync(string taxCode, Guid tenantId, CancellationToken cancellationToken = default);
    void Add(Vendor vendor);
    void Update(Vendor vendor);
}