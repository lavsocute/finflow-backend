using FinFlow.Api.GraphQL.Auth;
using FinFlow.Api.GraphQL.Documents;
using FinFlow.Application.Subscriptions.Commands.CancelSubscription;
using FinFlow.Application.Subscriptions.Commands.ChangeSubscriptionPlan;
using FinFlow.Domain.Enums;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using MediatR;

namespace FinFlow.Api.GraphQL.Subscriptions;

public sealed record SubscriptionMutationPayload(bool Success, string? Message);

[ExtendObjectType(typeof(AuthMutations))]
public sealed class SubscriptionsMutations
{
    /// <summary>
    /// Changes the current tenant plan. This is an in-product plan switch, not
    /// an external payment checkout integration.
    /// </summary>
    [Authorize(Roles = [nameof(RoleType.TenantAdmin)])]
    public async Task<SubscriptionMutationPayload> ChangeSubscriptionPlanAsync(
        string planTier,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var tenantId = DocumentsMutations.GetRequiredGuidClaim(
            context,
            "IdTenant",
            unauthorizedMessage: "The current user is not authorized to access this resource.");

        var normalized = planTier.Equals("Business", StringComparison.OrdinalIgnoreCase)
            ? nameof(PlanTier.Enterprise)
            : planTier;
        if (!Enum.TryParse<PlanTier>(normalized, ignoreCase: true, out var parsedTier))
            throw new GraphQLException(new HotChocolate.Error("Plan tier is invalid.", "TenantSubscription.InvalidPlanTier"));

        var result = await mediator.Send(
            new ChangeSubscriptionPlanCommand(tenantId, parsedTier),
            cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new SubscriptionMutationPayload(true, $"Subscription plan changed to {parsedTier}.");
    }

    /// <summary>
    /// Cancels the current tenant's subscription. The subscription remains usable
    /// until PeriodEnd, after which it transitions to Expired status.
    /// Only TenantAdmin role can cancel.
    /// </summary>
    [Authorize(Roles = [nameof(RoleType.TenantAdmin)])]
    public async Task<SubscriptionMutationPayload> CancelSubscriptionAsync(
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var tenantId = DocumentsMutations.GetRequiredGuidClaim(
            context,
            "IdTenant",
            unauthorizedMessage: "The current user is not authorized to access this resource.");

        var result = await mediator.Send(new CancelSubscriptionCommand(tenantId), cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new SubscriptionMutationPayload(true, "Subscription cancelled. Access remains until period end.");
    }
}
