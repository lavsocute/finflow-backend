using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Membership.Authorization;
using FinFlow.Application.Membership.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;

namespace FinFlow.Application.Membership.Queries.GetWorkspaceMembers;

public sealed class GetWorkspaceMembersQueryHandler : IQueryHandler<GetWorkspaceMembersQuery, Result<IReadOnlyList<MemberDto>>>
{
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IMembershipAuthorizationService _authorizationService;
    private readonly ICurrentTenant _currentTenant;

    public GetWorkspaceMembersQueryHandler(
        ITenantMembershipRepository membershipRepository,
        IAccountRepository accountRepository,
        IDepartmentRepository departmentRepository,
        IMembershipAuthorizationService authorizationService,
        ICurrentTenant currentTenant)
    {
        _membershipRepository = membershipRepository;
        _accountRepository = accountRepository;
        _departmentRepository = departmentRepository;
        _authorizationService = authorizationService;
        _currentTenant = currentTenant;
    }

    public async Task<Result<IReadOnlyList<MemberDto>>> Handle(GetWorkspaceMembersQuery request, CancellationToken cancellationToken)
    {
        var actor = await _membershipRepository.GetByIdAsync(request.ActorMembershipId, cancellationToken);
        if (actor is null)
            return Result.Failure<IReadOnlyList<MemberDto>>(Domain.Entities.TenantMembershipErrors.NotFound);

        var members = await _membershipRepository.GetByTenantIdAsync(request.TenantId, cancellationToken);

        var departments = (await _departmentRepository.GetByTenantIdAsync(request.TenantId, cancellationToken))
            .ToDictionary(d => d.Id, d => d.Name);

        var accountIds = members.Select(m => m.AccountId).Distinct().ToList();
        var accounts = new Dictionary<Guid, (string Email, string? FullName)>();
        foreach (var accountId in accountIds)
        {
            var account = await _accountRepository.GetByIdAsync(accountId, cancellationToken);
            if (account is not null)
            {
                accounts[accountId] = (account.Email, account.FullName);
            }
        }

        var filteredMembers = members
            .Where(m => request.DepartmentId is null || m.DepartmentId == request.DepartmentId)
            .Where(m => _authorizationService.CanViewMembers(
                actor,
                m.Id,
                actor.DepartmentId,
                m.DepartmentId))
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(m =>
            {
                accounts.TryGetValue(m.AccountId, out var accountInfo);
                departments.TryGetValue(m.DepartmentId ?? Guid.Empty, out var departmentName);

                var lastActive = m.Id == _currentTenant.MembershipId
                    ? DateTime.UtcNow
                    : (m.IsActive ? null : m.DeactivatedAt);

                return new MemberDto(
                    m.Id,
                    m.AccountId,
                    m.IdTenant,
                    m.DepartmentId,
                    accountInfo.FullName ?? accountInfo.Email.Split('@')[0],
                    accountInfo.Email,
                    m.DepartmentId.HasValue ? departmentName : null,
                    m.Role,
                    m.IsOwner,
                    m.IsActive,
                    m.CreatedAt,
                    lastActive,
                    m.DeactivatedAt,
                    m.DeactivatedBy,
                    m.DeactivatedReason);
            })
            .ToList();

        return Result.Success((IReadOnlyList<MemberDto>)filteredMembers);
    }
}