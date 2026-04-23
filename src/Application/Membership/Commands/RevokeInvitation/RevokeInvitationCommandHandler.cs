using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Invitations;

namespace FinFlow.Application.Membership.Commands.RevokeInvitation;

public sealed class RevokeInvitationCommandHandler : ICommandHandler<RevokeInvitationCommand, Result>
{
    private readonly IInvitationRepository _invitationRepository;

    public RevokeInvitationCommandHandler(IInvitationRepository invitationRepository)
    {
        _invitationRepository = invitationRepository;
    }

    public async Task<Result> Handle(RevokeInvitationCommand request, CancellationToken cancellationToken)
    {
        var invitation = await _invitationRepository.GetByIdAsync(request.InvitationId, cancellationToken);
        if (invitation is null)
            return Result.Failure(InvitationErrors.NotFound);

        if (invitation.IdTenant != request.TenantId)
            return Result.Failure(InvitationErrors.TenantRequired);

        return invitation.Revoke(request.ActorMembershipId);
    }
}
