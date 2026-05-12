using FinFlow.Domain.Documents;

namespace FinFlow.Application.Chat.Interfaces;

public interface IRerankService
{
    Task<IReadOnlyList<(DocumentChunk Chunk, float Score)>> RerankAsync(
        string query,
        IEnumerable<DocumentChunk> chunks,
        int topN = 5,
        CancellationToken ct = default);
}