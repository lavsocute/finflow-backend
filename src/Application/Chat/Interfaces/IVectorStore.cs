using FinFlow.Domain.Documents;

namespace FinFlow.Application.Chat.Interfaces;

public interface IVectorStore
{
    Task UpsertChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentChunk>> SearchAsync(
        float[] queryEmbedding,
        Guid tenantId,
        Guid? departmentId,
        Guid? ownerId,
        IReadOnlyCollection<DocumentChunkType>? allowedTypes = null,
        int topK = 20,
        CancellationToken ct = default);
    Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken ct = default);
}
