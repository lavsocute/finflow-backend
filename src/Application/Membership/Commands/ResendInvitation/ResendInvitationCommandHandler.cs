using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Invitations;

namespace FinFlow.Application.Membership.Commands.ResendInvitation;

public sealed class ResendInvitationCommandHandler : ICommandHandler<ResendInvitationCommand, Result>
{
    private readonly IInvitationRepository _invitationRepository;

    public ResendInvitationCommandHandler(IInvitationRepository invitationRepository)
    {
        _invitationRepository = invitationRepository;
    }

    public async Task<Result> Handle(ResendInvitationCommand request, CancellationToken cancellationToken)
    {
        var invitation = await _invitationRepository.GetByIdAsync(request.InvitationId, cancellationToken);
        if (invitation is null)
            return Result.Failure(InvitationErrors.NotFound);

        if (invitation.IdTenant != request.TenantId)
            return Result.Failure(InvitationErrors.TenantRequired);

        if (invitation.InvitedByMembershipId != request.ActorMembershipId)
            return Result.Failure(InvitationErrors.Forbidden);

        return invitation.Resend(request.NewExpiresAt, request.NewToken);
    }
}
