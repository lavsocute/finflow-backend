using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Membership.Authorization;
using FinFlow.Application.Membership.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;

namespace FinFlow.Application.Membership.Queries.GetMember;

public sealed class GetMemberQueryHandler : IQueryHandler<GetMemberQuery, Result<MemberDto>>
{
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IMembershipAuthorizationService _authorizationService;
    private readonly ICurrentTenant _currentTenant;

    public GetMemberQueryHandler(
        ITenantMembershipRepository membershipRepository,
        IMembershipAuthorizationService authorizationService,
        ICurrentTenant currentTenant)
    {
        _membershipRepository = membershipRepository;
        _authorizationService = authorizationService;
        _currentTenant = currentTenant;
    }

    public async Task<Result<MemberDto>> Handle(GetMemberQuery request, CancellationToken cancellationToken)
    {
        var membership = await _membershipRepository.GetByIdAsync(request.MembershipId, cancellationToken);
        if (membership is null)
            return Result.Failure<MemberDto>(TenantMembershipErrors.NotFound);

        var actor = await _membershipRepository.GetByIdAsync(request.ActorMembershipId, cancellationToken);
        if (actor is null)
            return Result.Failure<MemberDto>(TenantMembershipErrors.NotFound);

        if (!_authorizationService.CanViewMembers(
            request.ActorMembershipId,
            request.MembershipId,
            actor.Id,
            null))
            return Result.Failure<MemberDto>(TenantMembershipErrors.NotFound);

        var member = await _membershipRepository.GetByIdForUpdateAsync(request.MembershipId, cancellationToken);
        if (member is null)
            return Result.Failure<MemberDto>(TenantMembershipErrors.NotFound);

        return Result.Success(new MemberDto(
            member.Id,
            member.AccountId,
            member.IdTenant,
            member.DepartmentId,
            member.Role,
            member.IsOwner,
            member.IsActive,
            member.CreatedAt,
            member.DeactivatedAt,
            member.DeactivatedBy,
            member.DeactivatedReason));
    }
}
