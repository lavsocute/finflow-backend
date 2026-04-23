using FinFlow.Api.GraphQL.Membership;
using FinFlow.Application.Membership.Queries.GetMember;
using FinFlow.Application.Membership.Queries.GetWorkspaceMembers;
using HotChocolate;
using HotChocolate.Authorization;
using MediatR;

namespace FinFlow.Api.GraphQL.Platform;

[ExtendObjectType(typeof(global::Query))]
public sealed class PlatformQueries
{
    [Authorize(Roles = [nameof(FinFlow.Domain.Enums.RoleType.SuperAdmin)])]
    public async Task<IReadOnlyList<MemberType>> GetPlatformMembers(
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var query = new GetWorkspaceMembersQuery(Guid.Empty, Guid.Empty, null);
        var result = await mediator.Send(query, cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return result.Value.Select(MemberType.FromDto).ToList();
    }
}
