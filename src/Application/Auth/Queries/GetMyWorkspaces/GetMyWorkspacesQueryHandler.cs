using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Entities;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Tenants;

namespace FinFlow.Application.Auth.Queries.GetMyWorkspaces;

public sealed class GetMyWorkspacesQueryHandler : MediatR.IRequestHandler<GetMyWorkspacesQuery, Result<IReadOnlyList<MyWorkspaceResponse>>>
{
    private readonly IAccountRepository _accountRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly ITenantRepository _tenantRepository;

    public GetMyWorkspacesQueryHandler(
        IAccountRepository accountRepository,
        ITenantMembershipRepository membershipRepository,
        ITenantRepository tenantRepository)
    {
        _accountRepository = accountRepository;
        _membershipRepository = membershipRepository;
        _tenantRepository = tenantRepository;
    }

    public async Task<Result<IReadOnlyList<MyWorkspaceResponse>>> Handle(GetMyWorkspacesQuery request, CancellationToken cancellationToken)
    {
        var account = await _accountRepository.GetLoginInfoByIdAsync(request.AccountId, cancellationToken);
        if (account == null || !account.IsActive)
            return Result.Failure<IReadOnlyList<MyWorkspaceResponse>>(AccountErrors.Unauthorized);

        var memberships = await _membershipRepository.GetActiveByAccountIdAsync(request.AccountId, cancellationToken);
        if (memberships.Count == 0)
            return Result.Success<IReadOnlyList<MyWorkspaceResponse>>([]);

        var results = new List<MyWorkspaceResponse>(memberships.Count);

        foreach (var membership in memberships.OrderByDescending(x => x.IsOwner).ThenBy(x => x.CreatedAt))
        {
            var tenant = await _tenantRepository.GetByIdAsync(membership.IdTenant, cancellationToken);
            if (tenant == null)
                continue;

            results.Add(new MyWorkspaceResponse(
                membership.IdTenant,
                tenant.Id,
                tenant.TenantCode,
                tenant.Name,
                membership.Id,
                membership.Role));
        }

        return Result.Success<IReadOnlyList<MyWorkspaceResponse>>(results);
    }
}
