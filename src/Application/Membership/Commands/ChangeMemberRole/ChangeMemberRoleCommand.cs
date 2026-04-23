using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;

namespace FinFlow.Application.Membership.Commands.ChangeMemberRole;

public sealed record ChangeMemberRoleCommand(
    Guid MembershipId,
    Guid TenantId,
    Guid ActorMembershipId,
    RoleType NewRole) : ICommand<Result>;
