using FinFlow.Domain.Chat;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Application.Chat.Interfaces;

public interface IChatService
{
    Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(Guid sessionId, Guid membershipId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatSessionSummary>> GetSessionsAsync(Guid membershipId, int limit = 20, CancellationToken ct = default);
}

public record ChatRequest(
    Guid MembershipId,
    Guid TenantId,
    Guid? SessionId,
    string Query,
    Guid? DepartmentId
);

public record ChatResponse(
    string Answer,
    Guid SessionId,
    Guid MessageId,
    int DocumentCount,
    int TokenUsage
);