using FinFlow.Application.Common;
using FinFlow.Application.Membership.DTOs;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Membership.Queries.GetInvitations;

public sealed record GetInvitationsQuery(
    Guid TenantId,
    Guid ActorMembershipId,
    int Page = 1,
    int PageSize = 20) : IQuery<Result<IReadOnlyList<InvitationDto>>>;
