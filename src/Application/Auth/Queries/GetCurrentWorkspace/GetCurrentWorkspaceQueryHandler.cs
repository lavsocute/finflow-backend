using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Tenants;

namespace FinFlow.Application.Auth.Queries.GetCurrentWorkspace;

public sealed class GetCurrentWorkspaceQueryHandler : MediatR.IRequestHandler<GetCurrentWorkspaceQuery, Result<CurrentWorkspaceResponse>>
{
    private readonly IAccountRepository _accountRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly ITenantRepository _tenantRepository;

    public GetCurrentWorkspaceQueryHandler(
        IAccountRepository accountRepository,
        ITenantMembershipRepository membershipRepository,
        ITenantRepository tenantRepository)
    {
        _accountRepository = accountRepository;
        _membershipRepository = membershipRepository;
        _tenantRepository = tenantRepository;
    }

    public async Task<Result<CurrentWorkspaceResponse>> Handle(GetCurrentWorkspaceQuery request, CancellationToken cancellationToken)
    {
        var account = await _accountRepository.GetLoginInfoByIdAsync(request.AccountId, cancellationToken);
        if (account is null)
            return Result.Failure<CurrentWorkspaceResponse>(AccountErrors.NotFound);

        if (!account.IsActive)
            return Result.Failure<CurrentWorkspaceResponse>(AccountErrors.AlreadyDeactivated);

        var membershipResult = await ResolveMembershipAsync(request.AccountId, request.TenantId, request.MembershipId, cancellationToken);
        if (membershipResult.IsFailure)
            return Result.Failure<CurrentWorkspaceResponse>(membershipResult.Error);

        var membership = membershipResult.Value;
        var tenant = await _tenantRepository.GetByIdAsync(membership.IdTenant, cancellationToken);
        if (tenant is null)
            return Result.Failure<CurrentWorkspaceResponse>(TenantErrors.NotFound);

        return Result.Success(new CurrentWorkspaceResponse(
            account.Id,
            account.Email,
            membership.Id,
            membership.Role,
            tenant.Id,
            tenant.TenantCode,
            tenant.Name));
    }

    private async Task<Result<TenantMembershipSummary>> ResolveMembershipAsync(
        Guid accountId,
        Guid tenantId,
        Guid? membershipId,
        CancellationToken cancellationToken)
    {
        if (membershipId.HasValue)
        {
            var currentMembership = await _membershipRepository.GetByIdAsync(membershipId.Value, cancellationToken);
            if (currentMembership is null || !currentMembership.IsActive)
                return Result.Failure<TenantMembershipSummary>(TenantMembershipErrors.NotFound);

            if (currentMembership.AccountId != accountId || currentMembership.IdTenant != tenantId)
                return Result.Failure<TenantMembershipSummary>(AccountErrors.Unauthorized);

            return Result.Success(currentMembership);
        }

        var derivedMembership = await _membershipRepository.GetActiveByAccountAndTenantAsync(accountId, tenantId, cancellationToken);
        return derivedMembership is null
            ? Result.Failure<TenantMembershipSummary>(TenantMembershipErrors.NotFound)
            : Result.Success(derivedMembership);
    }
}
