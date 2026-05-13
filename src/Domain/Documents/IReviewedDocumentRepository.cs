using FinFlow.Domain.Enums;
using FinFlow.Domain.Entities;

namespace FinFlow.Domain.Documents;

public interface IReviewedDocumentRepository
{
    void Add(ReviewedDocument document);
    void Update(ReviewedDocument document);
    Task<ReviewedDocument?> GetByIdForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    Task<ReviewedDocument?> GetOwnedByIdAsync(Guid id, Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewedDocument>> GetAllActiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewedDocument>> GetReadyForApprovalAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewedDocument>> GetOwnedSubmittedAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewedDocument>> GetByStatusAsync(Guid tenantId, ApprovalStatusFilter status, string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<int> CountByStatusAsync(Guid tenantId, ApprovalStatusFilter status, string? search, CancellationToken cancellationToken = default);
}
