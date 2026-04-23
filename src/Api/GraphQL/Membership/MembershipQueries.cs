using FinFlow.Api.GraphQL.Membership;
using FinFlow.Application.Membership.Queries.GetInvitations;
using FinFlow.Application.Membership.Queries.GetMember;
using FinFlow.Application.Membership.Queries.GetWorkspaceMembers;
using FinFlow.Domain.Interfaces;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using MediatR;

namespace FinFlow.Api.GraphQL.Membership;

[ExtendObjectType(typeof(global::Query))]
public sealed class MembershipQueries
{
    [Authorize]
    public async Task<IReadOnlyList<MemberType>> GetWorkspaceMembers(
        Guid tenantId,
        Guid? departmentId,
        [Service] IMediator mediator,
        [Service] ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        var membershipId = currentTenant.MembershipId ?? Guid.Empty;
        var query = new GetWorkspaceMembersQuery(tenantId, membershipId, departmentId);
        var result = await mediator.Send(query, cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return result.Value.Select(MemberType.FromDto).ToList();
    }

    [Authorize]
    public async Task<MemberType?> GetMember(
        Guid membershipId,
        Guid tenantId,
        [Service] IMediator mediator,
        [Service] ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        var actorMembershipId = currentTenant.MembershipId ?? Guid.Empty;
        var query = new GetMemberQuery(membershipId, tenantId, actorMembershipId);
        var result = await mediator.Send(query, cancellationToken);

        if (result.IsFailure)
            return null;

        return MemberType.FromDto(result.Value);
    }

    [Authorize]
    public async Task<IReadOnlyList<InvitationType>> GetInvitations(
        Guid tenantId,
        [Service] IMediator mediator,
        [Service] ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        var membershipId = currentTenant.MembershipId ?? Guid.Empty;
        var query = new GetInvitationsQuery(tenantId, membershipId);
        var result = await mediator.Send(query, cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return result.Value.Select(InvitationType.FromDto).ToList();
    }
}
