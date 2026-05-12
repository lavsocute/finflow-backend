using FinFlow.Domain.Documents;

namespace FinFlow.Application.Chat.Interfaces;

public interface IChunkingService
{
    Task<IReadOnlyList<DocumentChunk>> ChunkAsync(
        Guid tenantId,
        string text,
        DocumentChunkType type,
        Guid documentId,
        Guid ownerMembershipId,
        Guid? departmentId,
        int chunkSize = 500,
        int overlap = 50,
        CancellationToken ct = default);
}
