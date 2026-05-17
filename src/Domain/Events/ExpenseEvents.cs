using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Events;

public sealed record ExpenseRejectedDomainEvent(
    Guid ExpenseId,
    Guid TenantId,
    Guid? RejectedByMembershipId,
    string Reason) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record ExpenseReopenedDomainEvent(
    Guid ExpenseId,
    Guid TenantId,
    Guid ReopenedByMembershipId,
    string Reason) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
