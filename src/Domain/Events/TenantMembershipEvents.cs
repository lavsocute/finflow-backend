using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;

namespace FinFlow.Domain.Events;

public sealed record TenantMembershipCreatedDomainEvent(
    Guid MembershipId,
    Guid AccountId,
    Guid IdTenant,
    RoleType Role) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record TenantMembershipRoleChangedDomainEvent(
    Guid MembershipId,
    Guid AccountId,
    Guid IdTenant,
    RoleType Role) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record TenantMembershipDeactivatedDomainEvent(
    Guid MembershipId,
    Guid AccountId,
    Guid IdTenant) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record TenantMembershipActivatedDomainEvent(
    Guid MembershipId,
    Guid AccountId,
    Guid IdTenant) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
