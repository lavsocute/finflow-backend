using FinFlow.Domain.Abstractions;
namespace FinFlow.Domain.Events;

public sealed record AccountCreatedDomainEvent(Guid AccountId, string Email) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record AccountDeactivatedDomainEvent(Guid AccountId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record AccountActivatedDomainEvent(Guid AccountId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

