namespace FinFlow.Application.Chat.Cascade;

public interface IChatIntentExemplarRepository
{
    Task<IReadOnlyList<ChatIntentExemplar>> GetActiveAsync(string embeddingModel, Guid? tenantId, CancellationToken ct);
    Task AddRangeAsync(IEnumerable<ChatIntentExemplar> exemplars, CancellationToken ct);
    Task RemoveAllAsync(Guid? tenantId, CancellationToken ct);
}
