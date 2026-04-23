using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.TenantMemberships;

namespace FinFlow.Application.Platform.Commands.PlatformTransferOwnership;

public sealed class PlatformTransferOwnershipCommandHandler : ICommandHandler<PlatformTransferOwnershipCommand, Result>
{
    private readonly ITenantMembershipRepository _membershipRepository;

    public PlatformTransferOwnershipCommandHandler(ITenantMembershipRepository membershipRepository)
    {
        _membershipRepository = membershipRepository;
    }

    public async Task<Result> Handle(PlatformTransferOwnershipCommand request, CancellationToken cancellationToken)
    {
        var newOwner = await _membershipRepository.GetByIdForUpdateAsync(request.MembershipId, cancellationToken);
        if (newOwner is null)
            return Result.Failure(TenantMembershipErrors.NotFound);

        if (newOwner.IdTenant != request.TenantId)
            return Result.Failure(TenantMembershipErrors.TenantRequired);

        newOwner.ChangeRole(Domain.Enums.RoleType.TenantAdmin);
        newOwner.SetIsOwner(true);

        return Result.Success();
    }
}
