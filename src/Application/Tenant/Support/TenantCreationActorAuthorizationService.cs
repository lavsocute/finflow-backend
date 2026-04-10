using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;

namespace FinFlow.Application.Tenant.Support;

public sealed class TenantCreationActorAuthorizationService
{
    private readonly IAccountRepository _accountRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly ICurrentTenant _currentTenant;

    public TenantCreationActorAuthorizationService(
        IAccountRepository accountRepository,
        ITenantMembershipRepository membershipRepository,
        ICurrentTenant currentTenant)
    {
        _accountRepository = accountRepository;
        _membershipRepository = membershipRepository;
        _currentTenant = currentTenant;
    }

    public async Task<Result> EnsureCanCreateTenantAsync(Guid accountId, Guid? currentMembershipId, CancellationToken cancellationToken)
    {
        var account = await _accountRepository.GetLoginInfoByIdAsync(accountId, cancellationToken);
        if (account == null || !account.IsActive)
            return Result.Failure(AccountErrors.Unauthorized);

        if (_currentTenant.IsSuperAdmin)
            return Result.Success();

        if (!currentMembershipId.HasValue)
            return Result.Success();

        var currentMembership = await _membershipRepository.GetByIdAsync(currentMembershipId.Value, cancellationToken);
        if (currentMembership == null || !currentMembership.IsActive)
            return Result.Failure(AccountErrors.Unauthorized);

        if (currentMembership.AccountId != accountId)
            return Result.Failure(AccountErrors.Unauthorized);

        if (currentMembership.Role != RoleType.TenantAdmin)
            return Result.Failure(TenantErrors.Forbidden);

        return Result.Success();
    }
}
