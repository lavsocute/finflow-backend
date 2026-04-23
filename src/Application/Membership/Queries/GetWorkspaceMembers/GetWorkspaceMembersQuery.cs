using FinFlow.Application.Common;
using FinFlow.Application.Membership.DTOs;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Membership.Queries.GetWorkspaceMembers;

public sealed record GetWorkspaceMembersQuery(
    Guid TenantId,
    Guid ActorMembershipId,
    Guid? DepartmentId,
    int Page = 1,
    int PageSize = 20) : IQuery<Result<IReadOnlyList<MemberDto>>>;
