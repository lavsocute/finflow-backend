using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Membership.Commands.ResendInvitation;

public sealed record ResendInvitationCommand(
    Guid InvitationId,
    Guid TenantId,
    Guid ActorMembershipId,
    DateTime NewExpiresAt,
    string NewToken) : ICommand<Result>;
