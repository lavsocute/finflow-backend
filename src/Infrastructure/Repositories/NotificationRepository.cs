using FinFlow.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class NotificationRepository : INotificationRepository
{
    private readonly ApplicationDbContext _dbContext;

    public NotificationRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<NotificationSummary>> GetByMembershipIdAsync(
        Guid membershipId,
        bool unreadOnly,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Notification>()
            .AsNoTracking()
            .Where(n => n.RecipientMembershipId == membershipId);

        if (unreadOnly)
            query = query.Where(n => !n.IsRead);

        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new NotificationSummary(
                n.Id,
                n.IdTenant,
                n.RecipientMembershipId,
                n.Type,
                n.Title,
                n.Body,
                n.PayloadJson,
                n.Severity,
                n.IsRead,
                n.ReadAt,
                n.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountUnreadAsync(Guid membershipId, CancellationToken cancellationToken = default) =>
        _dbContext.Set<Notification>()
            .AsNoTracking()
            .Where(n => n.RecipientMembershipId == membershipId && !n.IsRead)
            .CountAsync(cancellationToken);

    public Task<Notification?> GetEntityByIdAsync(Guid id, Guid recipientMembershipId, CancellationToken cancellationToken = default) =>
        _dbContext.Set<Notification>()
            .FirstOrDefaultAsync(n => n.Id == id && n.RecipientMembershipId == recipientMembershipId, cancellationToken);

    public Task<int> MarkAllAsReadAsync(Guid membershipId, CancellationToken cancellationToken = default) =>
        _dbContext.Set<Notification>()
            .Where(n => n.RecipientMembershipId == membershipId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, _ => true)
                .SetProperty(n => n.ReadAt, _ => DateTime.UtcNow),
                cancellationToken);

    public void Add(Notification notification) => _dbContext.Set<Notification>().Add(notification);
    public void AddRange(IEnumerable<Notification> notifications) => _dbContext.Set<Notification>().AddRange(notifications);
    public void Update(Notification notification) => _dbContext.Set<Notification>().Update(notification);
}
