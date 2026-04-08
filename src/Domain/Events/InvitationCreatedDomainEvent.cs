using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;

namespace FinFlow.Domain.Events;

public sealed record InvitationCreatedDomainEvent(
    Guid InvitationId,
    Guid IdTenant,
    Guid InvitedByMembershipId,
    string Email,
    RoleType Role,
    DateTime ExpiresAt) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
