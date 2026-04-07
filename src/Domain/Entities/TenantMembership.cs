using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Events;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class TenantMembership : Entity, IMultiTenant
{
    private TenantMembership(Guid id, Guid accountId, Guid idTenant, RoleType role)
    {
        Id = id;
        AccountId = accountId;
        IdTenant = idTenant;
        Role = role;
        CreatedAt = DateTime.UtcNow;
        IsActive = true;
    }

    private TenantMembership() { }

    public Guid AccountId { get; private set; }
    public Guid IdTenant { get; private set; }
    public RoleType Role { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<TenantMembership> Create(Guid accountId, Guid idTenant, RoleType role)
    {
        if (accountId == Guid.Empty)
            return Result.Failure<TenantMembership>(TenantMembershipErrors.AccountRequired);

        if (idTenant == Guid.Empty)
            return Result.Failure<TenantMembership>(TenantMembershipErrors.TenantRequired);

        var membership = new TenantMembership(Guid.NewGuid(), accountId, idTenant, role);
        membership.RaiseDomainEvent(new TenantMembershipCreatedDomainEvent(
            membership.Id,
            membership.AccountId,
            membership.IdTenant,
            membership.Role));

        return membership;
    }

    public Result ChangeRole(RoleType role)
    {
        if (Role == role)
            return Result.Failure(TenantMembershipErrors.SameRole);

        Role = role;
        RaiseDomainEvent(new TenantMembershipRoleChangedDomainEvent(Id, AccountId, IdTenant, role));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Result.Failure(TenantMembershipErrors.AlreadyDeactivated);

        IsActive = false;
        RaiseDomainEvent(new TenantMembershipDeactivatedDomainEvent(Id, AccountId, IdTenant));
        return Result.Success();
    }

    public Result Activate()
    {
        if (IsActive)
            return Result.Failure(TenantMembershipErrors.AlreadyActive);

        IsActive = true;
        RaiseDomainEvent(new TenantMembershipActivatedDomainEvent(Id, AccountId, IdTenant));
        return Result.Success();
    }
}
