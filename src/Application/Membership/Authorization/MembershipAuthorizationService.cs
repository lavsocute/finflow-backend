using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;

namespace FinFlow.Application.Membership.Authorization;

public interface IMembershipAuthorizationService
{
    bool CanViewMembers(Guid actorMembershipId, Guid targetMembershipId, Guid? actorDepartmentId, Guid? targetDepartmentId);
    bool CanInviteMember(Guid actorMembershipId, RoleType actorRole, Guid? actorDepartmentId, Guid targetDepartmentId);
    bool CanRemoveMember(Guid actorMembershipId, Guid targetMembershipId, RoleType actorRole);
    bool CanChangeMemberRole(Guid actorMembershipId, Guid targetMembershipId, RoleType actorRole);
    bool CanReactivateMember(Guid actorMembershipId, Guid targetMembershipId, RoleType actorRole);
}

public sealed class MembershipAuthorizationService : IMembershipAuthorizationService
{
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly ICurrentTenant _currentTenant;

    public MembershipAuthorizationService(
        ITenantMembershipRepository membershipRepository,
        ICurrentTenant currentTenant)
    {
        _membershipRepository = membershipRepository;
        _currentTenant = currentTenant;
    }

    public bool CanViewMembers(Guid actorMembershipId, Guid targetMembershipId, Guid? actorDepartmentId, Guid? targetDepartmentId)
    {
        if (actorMembershipId == targetMembershipId)
            return true;

        var actor = GetMembershipSummary(actorMembershipId);
        if (actor is null)
            return false;

        switch (actor.Role)
        {
            case RoleType.SuperAdmin:
                return true;
            case RoleType.TenantAdmin:
                return actor.IdTenant == _currentTenant.Id;
            case RoleType.Manager:
            case RoleType.Accountant:
                return actor.IdTenant == _currentTenant.Id && actorDepartmentId == targetDepartmentId;
            case RoleType.Staff:
            case RoleType.Guest:
                return actor.IdTenant == _currentTenant.Id && actorDepartmentId == targetDepartmentId;
            default:
                return false;
        }
    }

    public bool CanInviteMember(Guid actorMembershipId, RoleType actorRole, Guid? actorDepartmentId, Guid targetDepartmentId)
    {
        switch (actorRole)
        {
            case RoleType.SuperAdmin:
                return false;
            case RoleType.TenantAdmin:
                return true;
            case RoleType.Manager:
                return actorDepartmentId == targetDepartmentId;
            default:
                return false;
        }
    }

    public bool CanRemoveMember(Guid actorMembershipId, Guid targetMembershipId, RoleType actorRole)
    {
        if (actorRole == RoleType.SuperAdmin)
            return true;

        if (actorRole == RoleType.TenantAdmin)
            return true;

        return false;
    }

    public bool CanChangeMemberRole(Guid actorMembershipId, Guid targetMembershipId, RoleType actorRole)
    {
        if (actorRole == RoleType.SuperAdmin)
            return false;

        if (actorRole == RoleType.TenantAdmin)
            return true;

        return false;
    }

    public bool CanReactivateMember(Guid actorMembershipId, Guid targetMembershipId, RoleType actorRole)
    {
        if (actorRole == RoleType.SuperAdmin)
            return true;

        if (actorRole == RoleType.TenantAdmin)
            return true;

        return false;
    }

    private TenantMembershipSummary? GetMembershipSummary(Guid membershipId)
    {
        var task = _membershipRepository.GetByIdAsync(membershipId);
        task.Wait();
        return task.Result;
    }
}
