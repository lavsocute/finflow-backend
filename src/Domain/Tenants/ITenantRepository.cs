using FinFlow.Domain.Enums;

namespace FinFlow.Domain.Tenants;

public record TenantSummary(Guid Id, string Name, string TenantCode, TenancyModel TenancyModel, bool IsActive);

public interface ITenantRepository
{
    // Read Methods (DTO)
    Task<TenantSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TenantSummary?> GetByCodeAsync(string tenantCode, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCodeAsync(string tenantCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantSummary>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    // Write Methods (Entity)
    void Add(FinFlow.Domain.Entities.Tenant tenant);
    void Update(FinFlow.Domain.Entities.Tenant tenant);
    void Remove(FinFlow.Domain.Entities.Tenant tenant);
}
