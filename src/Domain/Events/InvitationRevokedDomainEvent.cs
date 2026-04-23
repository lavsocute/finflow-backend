using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Events;

public sealed record InvitationRevokedDomainEvent(
    Guid InvitationId,
    string Email,
    Guid IdTenant,
    Guid RevokedByMembershipId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
