using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Notifications;

namespace FinFlow.Application.Common.Notifications;

/// <summary>
/// Translates a domain event into zero or more <see cref="Notification"/>
/// rows that should be persisted alongside the audit log. Mirror of
/// <c>IDomainEventAuditMapper</c>; the two are dispatched together inside
/// <c>ApplicationDbContext.SaveChangesAsync</c> so notifications survive only
/// when the underlying business save succeeds.
///
/// Returns empty list when the event doesn't generate notifications. Some
/// events fan out to multiple recipients (e.g. duplicate flag → all managers).
/// </summary>
public interface IDomainEventNotificationMapper
{
    Task<IReadOnlyList<Notification>> MapAsync(
        IDomainEvent domainEvent,
        Guid? tenantId,
        CancellationToken cancellationToken);
}
