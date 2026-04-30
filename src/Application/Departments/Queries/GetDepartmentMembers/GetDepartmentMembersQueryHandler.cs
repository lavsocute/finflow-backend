using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Departments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Departments;
using FinFlow.Domain.TenantMemberships;

namespace FinFlow.Application.Departments.Queries.GetDepartmentMembers;

public sealed class GetDepartmentMembersQueryHandler : IQueryHandler<GetDepartmentMembersQuery, Result<IReadOnlyList<MemberDto>>>
{
    private readonly IDepartmentRepository _departmentRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IAccountRepository _accountRepository;

    public GetDepartmentMembersQueryHandler(
        IDepartmentRepository departmentRepository,
        ITenantMembershipRepository membershipRepository,
        IAccountRepository accountRepository)
    {
        _departmentRepository = departmentRepository;
        _membershipRepository = membershipRepository;
        _accountRepository = accountRepository;
    }

    public async Task<Result<IReadOnlyList<MemberDto>>> Handle(GetDepartmentMembersQuery request, CancellationToken cancellationToken)
    {
        var department = await _departmentRepository.GetByIdAsync(request.DepartmentId, cancellationToken);
        if (department is null || department.IdTenant != request.TenantId)
            return Result.Failure<IReadOnlyList<MemberDto>>(Domain.Entities.DepartmentErrors.NotFound);

        var memberships = await _membershipRepository.GetByTenantIdAsync(request.TenantId, cancellationToken);

        var departmentMemberships = memberships
            .Where(m => m.DepartmentId == request.DepartmentId)
            .ToList();

        var memberDtos = new List<MemberDto>();

        foreach (var membership in departmentMemberships)
        {
            var account = await _accountRepository.GetByIdAsync(membership.AccountId, cancellationToken);
            if (account is not null)
            {
                memberDtos.Add(new MemberDto(
                    membership.Id,
                    account.Email,
                    membership.Role.ToString(),
                    membership.IsActive));
            }
        }

        return Result.Success((IReadOnlyList<MemberDto>)memberDtos);
    }
}