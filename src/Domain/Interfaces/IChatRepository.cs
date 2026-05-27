using FinFlow.Domain.Chat;

namespace FinFlow.Domain.Interfaces;

public interface IChatRepository
{
    Task<ChatSession?> GetSessionByIdAndMembershipAsync(Guid sessionId, Guid membershipId, CancellationToken ct = default);
    Task<ChatSession?> GetOwnedSessionAsync(Guid sessionId, Guid tenantId, Guid membershipId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetMessagesBySessionAsync(Guid sessionId, CancellationToken ct = default);
    Task AddSessionAsync(ChatSession session, CancellationToken ct = default);
    Task UpdateSessionAsync(ChatSession session, CancellationToken ct = default);
    Task AddMessageAsync(ChatMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatSessionSummary>> GetSessionsAsync(Guid membershipId, int limit, CancellationToken ct = default);

    // Session persistence for context management
    Task<IReadOnlyList<ChatSession>> GetActiveSessionsAsync(DateTime cutoff, CancellationToken ct = default);
    Task<int> DeleteExpiredAsync(DateTime cutoff, CancellationToken ct = default);
}

public record ChatSessionSummary(
    Guid Id,
    string Title,
    int MessageCount,
    DateTime? LastMessageAt
);