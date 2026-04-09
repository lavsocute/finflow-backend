using FinFlow.Domain.Enums;

namespace FinFlow.Domain.TenantApprovals;

public record TenantApprovalRequestSummary(
    Guid Id,
    string TenantCode,
    string Name,
    string CompanyName,
    string TaxCode,
    int? EmployeeCount,
    string Currency,
    TenancyModel TenancyModel,
    Guid RequestedById,
    TenantApprovalStatus Status,
    DateTime ExpiresAt,
    DateTime CreatedAt);

public interface ITenantApprovalRequestRepository
{
    Task<TenantApprovalRequestSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantApprovalRequestSummary>> GetPendingAsync(CancellationToken cancellationToken = default);
    Task<bool> ExistsPendingByTenantCodeAsync(string tenantCode, CancellationToken cancellationToken = default);
    Task<bool> IsTenantCodeBlockedAsync(string tenantCode, DateTime asOfUtc, CancellationToken cancellationToken = default);
    Task<bool> ExistsPendingByRequestedByAsync(Guid requestedById, CancellationToken cancellationToken = default);
    Task<FinFlow.Domain.Entities.TenantApprovalRequest?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(FinFlow.Domain.Entities.TenantApprovalRequest request);
    void Update(FinFlow.Domain.Entities.TenantApprovalRequest request);
}
