using FinFlow.Domain.Entities;

namespace FinFlow.Domain.Documents;

public interface IReviewedDocumentRepository
{
    void Add(ReviewedDocument document);
    void Update(ReviewedDocument document);
    Task<ReviewedDocument?> GetByIdForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewedDocument>> GetReadyForApprovalAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewedDocument>> GetOwnedSubmittedAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default);
}
