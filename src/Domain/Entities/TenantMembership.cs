using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Events;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class TenantMembership : Entity, IMultiTenant
{
    private TenantMembership(Guid id, Guid accountId, Guid idTenant, RoleType role, bool isOwner)
    {
        Id = id;
        AccountId = accountId;
        IdTenant = idTenant;
        Role = role;
        IsOwner = isOwner;
        CreatedAt = DateTime.UtcNow;
        IsActive = true;
    }

    private TenantMembership() { }

    public Guid AccountId { get; private set; }
    public Guid IdTenant { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public RoleType Role { get; private set; }
    public bool IsOwner { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime? DeactivatedAt { get; private set; }
    public Guid? DeactivatedBy { get; private set; }
    public string? DeactivatedReason { get; private set; }

    public static Result<TenantMembership> Create(Guid accountId, Guid idTenant, RoleType role, bool isOwner = false)
    {
        if (accountId == Guid.Empty)
            return Result.Failure<TenantMembership>(TenantMembershipErrors.AccountRequired);

        if (idTenant == Guid.Empty)
            return Result.Failure<TenantMembership>(TenantMembershipErrors.TenantRequired);

        if (isOwner && role != RoleType.TenantAdmin)
            return Result.Failure<TenantMembership>(TenantMembershipErrors.OwnerMustBeTenantAdmin);

        var membership = new TenantMembership(Guid.NewGuid(), accountId, idTenant, role, isOwner);
        membership.RaiseDomainEvent(new TenantMembershipCreatedDomainEvent(
            membership.Id,
            membership.AccountId,
            membership.IdTenant,
            membership.Role,
            membership.IsOwner));

        return membership;
    }

    public Result ChangeRole(RoleType role)
    {
        if (Role == role)
            return Result.Failure(TenantMembershipErrors.SameRole);

        if (role == RoleType.Staff && !DepartmentId.HasValue)
            return Result.Failure(TenantMembershipErrors.DepartmentRequired);

        Role = role;
        RaiseDomainEvent(new TenantMembershipRoleChangedDomainEvent(Id, AccountId, IdTenant, role));
        return Result.Success();
    }

    public void SetDepartment(Guid? departmentId)
    {
        DepartmentId = departmentId;
    }

    public Result Deactivate(Guid deactivatedBy, string? reason)
    {
        if (!IsActive)
            return Result.Failure(TenantMembershipErrors.AlreadyDeactivated);

        IsActive = false;
        DeactivatedAt = DateTime.UtcNow;
        DeactivatedBy = deactivatedBy;
        DeactivatedReason = reason;
        RaiseDomainEvent(new TenantMembershipDeactivatedDomainEvent(Id, AccountId, IdTenant, deactivatedBy));
        return Result.Success();
    }

    public Result Activate()
    {
        if (IsActive)
            return Result.Failure(TenantMembershipErrors.AlreadyActive);

        IsActive = true;
        DeactivatedAt = null;
        DeactivatedBy = null;
        DeactivatedReason = null;
        RaiseDomainEvent(new TenantMembershipActivatedDomainEvent(Id, AccountId, IdTenant));
        return Result.Success();
    }

    public void SetIsOwner(bool isOwner)
    {
        IsOwner = isOwner;
    }
}
