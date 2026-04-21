using FinFlow.Application.Subscriptions.DTOs.Responses;
using FinFlow.Application.Subscriptions.Queries.GetCurrentSubscription;
using FinFlow.Api.GraphQL.Documents;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using MediatR;

namespace FinFlow.Api.GraphQL.Subscriptions;

[ExtendObjectType(typeof(global::Query))]
public sealed class SubscriptionsQueries
{
    [Authorize]
    public async Task<CurrentSubscriptionResponse> CurrentSubscriptionAsync(
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var tenantId = DocumentsMutations.GetRequiredGuidClaim(
            context,
            "IdTenant",
            unauthorizedMessage: "The current user is not authorized to access this resource.");

        var result = await mediator.Send(new GetCurrentSubscriptionQuery(tenantId), cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return result.Value;
    }
}
