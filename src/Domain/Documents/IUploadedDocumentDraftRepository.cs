using FinFlow.Domain.Entities;

namespace FinFlow.Domain.Documents;

public interface IUploadedDocumentDraftRepository
{
    void Add(UploadedDocumentDraft draft);
    void Update(UploadedDocumentDraft draft);
    Task<UploadedDocumentDraft?> GetByIdAsync(Guid id, Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UploadedDocumentDraft>> GetOwnedActiveAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default);
}
