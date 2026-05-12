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

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesBySessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        return await _dbContext.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
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
        var sessions = await _dbContext.ChatSessions
            .Where(s => s.MembershipId == membershipId)
            .OrderByDescending(s => s.UpdatedAt)
            .Take(limit)
            .ToListAsync(ct);

        var result = new List<ChatSessionSummary>();
        foreach (var session in sessions)
        {
            var messageCount = await _dbContext.ChatMessages
                .CountAsync(m => m.SessionId == session.Id, ct);

            var lastMessage = await _dbContext.ChatMessages
                .Where(m => m.SessionId == session.Id)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync(ct);

            result.Add(new ChatSessionSummary(
                session.Id,
                session.Title,
                messageCount,
                lastMessage?.CreatedAt));
        }

        return result;
    }
}
