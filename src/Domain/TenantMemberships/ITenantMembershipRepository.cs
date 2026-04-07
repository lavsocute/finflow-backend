using FinFlow.Domain.Enums;

namespace FinFlow.Domain.TenantMemberships;

public record TenantMembershipSummary(
    Guid Id,
    Guid AccountId,
    Guid IdTenant,
    RoleType Role,
    bool IsActive,
    DateTime CreatedAt);

public interface ITenantMembershipRepository
{
    Task<TenantMembershipSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantMembershipSummary>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantMembershipSummary>> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid accountId, Guid idTenant, CancellationToken cancellationToken = default);

    Task<FinFlow.Domain.Entities.TenantMembership?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(FinFlow.Domain.Entities.TenantMembership membership);
    void Update(FinFlow.Domain.Entities.TenantMembership membership);
}
