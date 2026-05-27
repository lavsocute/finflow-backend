using FinFlow.Domain.Documents;

namespace FinFlow.Application.Chat.Interfaces;

public interface IChatResponseCacheKeyBuilder
{
    string Build(
        Guid tenantId,
        Guid membershipId,
        string role,
        Guid? departmentId,
        Guid? ownerFilter,
        IReadOnlyCollection<DocumentChunkType> allowedTypes,
        string query,
        string promptVersion);

    bool IsCacheable(string query);
}