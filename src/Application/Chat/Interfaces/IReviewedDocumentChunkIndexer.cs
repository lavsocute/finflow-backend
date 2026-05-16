using FinFlow.Domain.Entities;

namespace FinFlow.Application.Chat.Interfaces;

public interface IReviewedDocumentChunkIndexer
{
    Task<int> ReindexAsync(ReviewedDocument document, CancellationToken cancellationToken = default);
    Task<int> RemoveAsync(Guid documentId, CancellationToken cancellationToken = default);
}
