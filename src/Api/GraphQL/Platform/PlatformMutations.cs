using FinFlow.Application.Platform.Commands.PlatformRemoveMember;
using FinFlow.Application.Platform.Commands.PlatformTransferOwnership;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace FinFlow.Api.GraphQL.Platform;

public record PlatformRemoveMemberInput(Guid MembershipId, string Reason);
public record PlatformTransferOwnershipInput(Guid MembershipId, Guid TenantId);

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
}
