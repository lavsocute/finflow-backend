using FinFlow.Application.Platform.Commands.PlatformRemoveMember;
using FinFlow.Application.Platform.Commands.PlatformTransferOwnership;
using FinFlow.Application.Subscriptions.Commands.PauseSubscription;
using FinFlow.Application.Subscriptions.Commands.ReactivateSubscription;
using FinFlow.Application.Subscriptions.Commands.ResumeSubscription;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace FinFlow.Api.GraphQL.Platform;

public record PlatformRemoveMemberInput(Guid MembershipId, string Reason);
public record PlatformTransferOwnershipInput(Guid MembershipId, Guid TenantId);
public record PlatformSubscriptionStateInput(Guid TenantId);

public record PlatformMutationPayload(bool Success, string? Message);

public sealed class PlatformMutations
{
    [Authorize(Roles = [nameof(FinFlow.Domain.Enums.RoleType.SuperAdmin)])]
    public async Task<PlatformMutationPayload> PlatformRemoveMemberAsync(
        PlatformRemoveMemberInput input,
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new PlatformRemoveMemberCommand(input.MembershipId, input.Reason),
            cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new PlatformMutationPayload(true, null);
    }

    [Authorize(Roles = [nameof(FinFlow.Domain.Enums.RoleType.SuperAdmin)])]
    public async Task<PlatformMutationPayload> PlatformTransferOwnershipAsync(
        PlatformTransferOwnershipInput input,
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new PlatformTransferOwnershipCommand(input.MembershipId, input.TenantId),
            cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new PlatformMutationPayload(true, null);
    }

    /// <summary>Admin action: pause a tenant's subscription (e.g., support hold). No quota during pause.</summary>
    [Authorize(Roles = [nameof(FinFlow.Domain.Enums.RoleType.SuperAdmin)])]
    public async Task<PlatformMutationPayload> PlatformPauseSubscriptionAsync(
        PlatformSubscriptionStateInput input,
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new PauseSubscriptionCommand(input.TenantId), cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new PlatformMutationPayload(true, "Subscription paused.");
    }

    /// <summary>Admin action: resume a paused subscription.</summary>
    [Authorize(Roles = [nameof(FinFlow.Domain.Enums.RoleType.SuperAdmin)])]
    public async Task<PlatformMutationPayload> PlatformResumeSubscriptionAsync(
        PlatformSubscriptionStateInput input,
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ResumeSubscriptionCommand(input.TenantId), cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new PlatformMutationPayload(true, "Subscription resumed.");
    }

    /// <summary>Admin action: reactivate a PastDue/Expired subscription (e.g., after manual payment processing).</summary>
    [Authorize(Roles = [nameof(FinFlow.Domain.Enums.RoleType.SuperAdmin)])]
    public async Task<PlatformMutationPayload> PlatformReactivateSubscriptionAsync(
        PlatformSubscriptionStateInput input,
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ReactivateSubscriptionCommand(input.TenantId), cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new PlatformMutationPayload(true, "Subscription reactivated. Period renewed from now.");
    }
}
