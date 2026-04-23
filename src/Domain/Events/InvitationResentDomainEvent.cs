using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Events;

public sealed record InvitationResentDomainEvent(
    Guid InvitationId,
    string Email,
    Guid IdTenant,
    Guid InvitedByMembershipId,
    DateTime ExpiresAt) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
