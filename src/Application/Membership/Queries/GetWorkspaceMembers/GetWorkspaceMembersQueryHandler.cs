using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Membership.Authorization;
using FinFlow.Application.Membership.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;

namespace FinFlow.Application.Membership.Queries.GetWorkspaceMembers;

public sealed class GetWorkspaceMembersQueryHandler : IQueryHandler<GetWorkspaceMembersQuery, Result<IReadOnlyList<MemberDto>>>
{
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IMembershipAuthorizationService _authorizationService;
    private readonly ICurrentTenant _currentTenant;

    public GetWorkspaceMembersQueryHandler(
        ITenantMembershipRepository membershipRepository,
        IMembershipAuthorizationService authorizationService,
        ICurrentTenant currentTenant)
    {
        _membershipRepository = membershipRepository;
        _authorizationService = authorizationService;
        _currentTenant = currentTenant;
    }

    public async Task<Result<IReadOnlyList<MemberDto>>> Handle(GetWorkspaceMembersQuery request, CancellationToken cancellationToken)
    {
        var actor = await _membershipRepository.GetByIdAsync(request.ActorMembershipId, cancellationToken);
        if (actor is null)
            return Result.Failure<IReadOnlyList<MemberDto>>(Domain.Entities.TenantMembershipErrors.NotFound);

        var members = await _membershipRepository.GetByTenantIdAsync(request.TenantId, cancellationToken);

        var filteredMembers = members
            .Where(m => request.DepartmentId is null || true)
            .Where(m => _authorizationService.CanViewMembers(
                request.ActorMembershipId,
                m.Id,
                actor.Id,
                request.DepartmentId))
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(m => new MemberDto(
                m.Id,
                m.AccountId,
                m.IdTenant,
                null,
                m.Role,
                m.IsOwner,
                m.IsActive,
                m.CreatedAt,
                null,
                null,
                null))
            .ToList();

        return Result.Success((IReadOnlyList<MemberDto>)filteredMembers);
    }
}
