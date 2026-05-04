using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Membership.Authorization;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;

namespace FinFlow.Application.Membership.Commands.RemoveMember;

public sealed class RemoveMemberCommandHandler : ICommandHandler<RemoveMemberCommand, Result>
{
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IMembershipAuthorizationService _authorizationService;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;

    public RemoveMemberCommandHandler(
        ITenantMembershipRepository membershipRepository,
        IMembershipAuthorizationService authorizationService,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork)
    {
        _membershipRepository = membershipRepository;
        _authorizationService = authorizationService;
        _currentTenant = currentTenant;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemoveMemberCommand request, CancellationToken cancellationToken)
    {
        var membership = await _membershipRepository.GetByIdForUpdateAsync(request.MembershipId, cancellationToken);
        if (membership is null)
            return Result.Failure(TenantMembershipErrors.NotFound);

        var actor = await _membershipRepository.GetByIdAsync(request.ActorMembershipId, cancellationToken);
        if (actor is null)
            return Result.Failure(TenantMembershipErrors.NotFound);

        if (!_authorizationService.CanRemoveMember(request.ActorMembershipId, request.MembershipId, actor.Role))
            return Result.Failure(TenantMembershipErrors.Forbidden);

        if (membership.IdTenant != _currentTenant.Id)
            return Result.Failure(TenantMembershipErrors.Forbidden);

        if (membership.IsOwner)
            return Result.Failure(TenantMembershipErrors.OwnerMustBeTenantAdmin);

        var deactivateResult = membership.Deactivate(request.ActorMembershipId, request.Reason);
        if (deactivateResult.IsFailure)
            return deactivateResult;

        _membershipRepository.Update(membership);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
