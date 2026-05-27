using FinFlow.Domain.Chat;
using FinFlow.Domain.Interfaces;
using FinFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

public class ChatRepository : IChatRepository
{
    private readonly ApplicationDbContext _dbContext;

    public ChatRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ChatSession?> GetSessionByIdAndMembershipAsync(Guid sessionId, Guid membershipId, CancellationToken ct = default)
    {
        return await _dbContext.ChatSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.MembershipId == membershipId, ct);
    }

    public async Task<ChatSession?> GetOwnedSessionAsync(Guid sessionId, Guid tenantId, Guid membershipId, CancellationToken ct = default)
    {
        return await _dbContext.ChatSessions
            .FirstOrDefaultAsync(
                s => s.Id == sessionId && s.IdTenant == tenantId && s.MembershipId == membershipId,
                ct);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesBySessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        return await _dbContext.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Role)
            .ToListAsync(ct);
    }

    public async Task AddSessionAsync(ChatSession session, CancellationToken ct = default)
    {
        _dbContext.ChatSessions.Add(session);
        await Task.CompletedTask;
    }

    public async Task UpdateSessionAsync(ChatSession session, CancellationToken ct = default)
    {
        _dbContext.ChatSessions.Update(session);
        await Task.CompletedTask;
    }

    public async Task AddMessageAsync(ChatMessage message, CancellationToken ct = default)
    {
        _dbContext.ChatMessages.Add(message);
        await Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ChatSessionSummary>> GetSessionsAsync(Guid membershipId, int limit, CancellationToken ct = default)
    {
        var result = await _dbContext.ChatSessions
            .Where(s => s.MembershipId == membershipId)
            .OrderByDescending(s => s.UpdatedAt)
            .Take(limit)
            .Select(s => new ChatSessionSummary(
                s.Id,
                s.Title,
                _dbContext.ChatMessages.Count(m => m.SessionId == s.Id),
                _dbContext.ChatMessages
                    .Where(m => m.SessionId == s.Id)
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => (DateTime?)m.CreatedAt)
                    .FirstOrDefault()))
            .ToListAsync(ct);

        return result;
    }

    public async Task<IReadOnlyList<ChatSession>> GetActiveSessionsAsync(DateTime cutoff, CancellationToken ct = default)
    {
        return await _dbContext.ChatSessions
            .Where(s => s.UpdatedAt >= cutoff && s.IsActive)
            .ToListAsync(ct);
    }

    public async Task<int> DeleteExpiredAsync(DateTime cutoff, CancellationToken ct = default)
    {
        var expiredSessions = await _dbContext.ChatSessions
            .Where(s => s.UpdatedAt < cutoff && !s.IsActive)
            .ToListAsync(ct);

        _dbContext.ChatSessions.RemoveRange(expiredSessions);

        return expiredSessions.Count;
    }
}
