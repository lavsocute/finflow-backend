namespace FinFlow.Domain.Notifications;

public record NotificationSummary(
    Guid Id,
    Guid IdTenant,
    Guid RecipientMembershipId,
    string Type,
    string Title,
    string Body,
    string PayloadJson,
    NotificationSeverity Severity,
    bool IsRead,
    DateTime? ReadAt,
    DateTime CreatedAt);

public interface INotificationRepository
{
    Task<IReadOnlyList<NotificationSummary>> GetByMembershipIdAsync(
        Guid membershipId,
        bool unreadOnly,
        int limit,
        CancellationToken cancellationToken = default);

    Task<int> CountUnreadAsync(
        Guid membershipId,
        CancellationToken cancellationToken = default);

    Task<Notification?> GetEntityByIdAsync(
        Guid id,
        Guid recipientMembershipId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-mark all unread notifications of a recipient as read in a single
    /// SQL update. Returns the number of rows affected.
    /// </summary>
    Task<int> MarkAllAsReadAsync(
        Guid membershipId,
        CancellationToken cancellationToken = default);

    void Add(Notification notification);
    void AddRange(IEnumerable<Notification> notifications);
    void Update(Notification notification);
}
