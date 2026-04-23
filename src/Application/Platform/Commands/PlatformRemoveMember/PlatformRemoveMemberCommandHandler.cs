using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.TenantMemberships;

namespace FinFlow.Application.Platform.Commands.PlatformRemoveMember;

public sealed class PlatformRemoveMemberCommandHandler : ICommandHandler<PlatformRemoveMemberCommand, Result>
{
    private readonly ITenantMembershipRepository _membershipRepository;

    public PlatformRemoveMemberCommandHandler(ITenantMembershipRepository membershipRepository)
    {
        _membershipRepository = membershipRepository;
    }

    public async Task<Result> Handle(PlatformRemoveMemberCommand request, CancellationToken cancellationToken)
    {
        var membership = await _membershipRepository.GetByIdForUpdateAsync(request.MembershipId, cancellationToken);
        if (membership is null)
            return Result.Failure(TenantMembershipErrors.NotFound);

        if (membership.IsOwner)
            return Result.Failure(TenantMembershipErrors.OwnerMustBeTenantAdmin);

        return membership.Deactivate(Guid.Empty, request.Reason);
    }
}
