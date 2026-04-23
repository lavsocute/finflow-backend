using FinFlow.Application.Membership.Commands.ChangeMemberRole;
using FinFlow.Application.Membership.Commands.ReactivateMember;
using FinFlow.Application.Membership.Commands.RemoveMember;
using FinFlow.Application.Membership.Commands.ResendInvitation;
using FinFlow.Application.Membership.Commands.RevokeInvitation;
using FinFlow.Domain.Enums;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace FinFlow.Api.GraphQL.Membership;

public record ChangeMemberRoleInput(Guid MembershipId, RoleType NewRole);
public record RemoveMemberInput(Guid MembershipId, string? Reason);
public record ResendInvitationInput(Guid InvitationId, DateTime NewExpiresAt, string NewToken);
public record RevokeInvitationInput(Guid InvitationId);

public record MemberMutationPayload(bool Success, string? Message);

public sealed class MembershipMutations
{
    [Authorize]
    public async Task<MemberMutationPayload> ChangeMemberRoleAsync(
        ChangeMemberRoleInput input,
        Guid tenantId,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var membershipId = GetMembershipId(context);
        if (membershipId == null)
            throw new GraphQLException(new HotChocolate.Error("User is not authenticated", "Account.Unauthorized"));

        var result = await mediator.Send(
            new ChangeMemberRoleCommand(input.MembershipId, tenantId, membershipId.Value, input.NewRole),
            cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new MemberMutationPayload(true, null);
    }

    [Authorize]
    public async Task<MemberMutationPayload> RemoveMemberAsync(
        RemoveMemberInput input,
        Guid tenantId,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var membershipId = GetMembershipId(context);
        if (membershipId == null)
            throw new GraphQLException(new HotChocolate.Error("User is not authenticated", "Account.Unauthorized"));

        var result = await mediator.Send(
            new RemoveMemberCommand(input.MembershipId, tenantId, membershipId.Value, input.Reason),
            cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new MemberMutationPayload(true, null);
    }

    [Authorize]
    public async Task<MemberMutationPayload> ReactivateMemberAsync(
        Guid membershipId,
        Guid tenantId,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var actorMembershipId = GetMembershipId(context);
        if (actorMembershipId == null)
            throw new GraphQLException(new HotChocolate.Error("User is not authenticated", "Account.Unauthorized"));

        var result = await mediator.Send(
            new ReactivateMemberCommand(membershipId, tenantId, actorMembershipId.Value),
            cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new MemberMutationPayload(true, null);
    }

    [Authorize]
    public async Task<MemberMutationPayload> ResendInvitationAsync(
        ResendInvitationInput input,
        Guid tenantId,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var membershipId = GetMembershipId(context);
        if (membershipId == null)
            throw new GraphQLException(new HotChocolate.Error("User is not authenticated", "Account.Unauthorized"));

        var result = await mediator.Send(
            new ResendInvitationCommand(input.InvitationId, tenantId, membershipId.Value, input.NewExpiresAt, input.NewToken),
            cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new MemberMutationPayload(true, null);
    }

    [Authorize]
    public async Task<MemberMutationPayload> RevokeInvitationAsync(
        RevokeInvitationInput input,
        Guid tenantId,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var membershipId = GetMembershipId(context);
        if (membershipId == null)
            throw new GraphQLException(new HotChocolate.Error("User is not authenticated", "Account.Unauthorized"));

        var result = await mediator.Send(
            new RevokeInvitationCommand(input.InvitationId, tenantId, membershipId.Value),
            cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new MemberMutationPayload(true, null);
    }

    private static Guid? GetMembershipId(IResolverContext context)
    {
        var httpContextAccessor = context.Service<IHttpContextAccessor>();
        var user = httpContextAccessor.HttpContext?.User;

        var membershipIdClaim = user?.FindFirst("MembershipId")?.Value;
        if (Guid.TryParse(membershipIdClaim, out var membershipId))
            return membershipId;

        return null;
    }
}
