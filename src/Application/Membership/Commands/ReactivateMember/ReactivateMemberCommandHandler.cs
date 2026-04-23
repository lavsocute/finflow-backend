using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Membership.Authorization;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;

namespace FinFlow.Application.Membership.Commands.ReactivateMember;

public sealed class ReactivateMemberCommandHandler : ICommandHandler<ReactivateMemberCommand, Result>
{
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IMembershipAuthorizationService _authorizationService;
    private readonly ICurrentTenant _currentTenant;

    public ReactivateMemberCommandHandler(
        ITenantMembershipRepository membershipRepository,
        IMembershipAuthorizationService authorizationService,
        ICurrentTenant currentTenant)
    {
        _membershipRepository = membershipRepository;
        _authorizationService = authorizationService;
        _currentTenant = currentTenant;
    }

    public async Task<Result> Handle(ReactivateMemberCommand request, CancellationToken cancellationToken)
    {
        var membership = await _membershipRepository.GetByIdForUpdateAsync(request.MembershipId, cancellationToken);
        if (membership is null)
            return Result.Failure(TenantMembershipErrors.NotFound);

        var actor = await _membershipRepository.GetByIdAsync(request.ActorMembershipId, cancellationToken);
        if (actor is null)
            return Result.Failure(TenantMembershipErrors.NotFound);

        if (!_authorizationService.CanReactivateMember(request.ActorMembershipId, request.MembershipId, actor.Role))
            return Result.Failure(TenantMembershipErrors.NotFound);

        if (membership.IdTenant != _currentTenant.Id)
            return Result.Failure(TenantMembershipErrors.NotFound);

        return membership.Activate();
    }
}
