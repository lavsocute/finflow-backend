using FinFlow.Application.Common;
using FinFlow.Application.Membership.DTOs;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Membership.Queries.GetMember;

public sealed record GetMemberQuery(
    Guid MembershipId,
    Guid TenantId,
    Guid ActorMembershipId) : IQuery<Result<MemberDto>>;
