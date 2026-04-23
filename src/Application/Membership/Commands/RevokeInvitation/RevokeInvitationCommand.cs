using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Membership.Commands.RevokeInvitation;

public sealed record RevokeInvitationCommand(
    Guid InvitationId,
    Guid TenantId,
    Guid ActorMembershipId) : ICommand<Result>;
