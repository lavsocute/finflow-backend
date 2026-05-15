using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Tenant.Support;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.RefreshTokens;
using FinFlow.Domain.TenantApprovals;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Tenants;
using TenantEntity = FinFlow.Domain.Entities.Tenant;

namespace FinFlow.Application.Tenant.Commands.CreateSharedTenant;

public sealed class CreateSharedTenantCommandHandler : MediatR.IRequestHandler<CreateSharedTenantCommand, Result<AuthResponse>>
{
    private readonly TenantCreationActorAuthorizationService _authorizationService;
    private readonly IAccountRepository _accountRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantApprovalRequestRepository _tenantApprovalRequestRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly ICurrentTenant _currentTenant;

    public CreateSharedTenantCommandHandler(
        TenantCreationActorAuthorizationService authorizationService,
        IAccountRepository accountRepository,
        ITenantRepository tenantRepository,
        ITenantApprovalRequestRepository tenantApprovalRequestRepository,
        ITenantMembershipRepository membershipRepository,
        IDepartmentRepository departmentRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        ICurrentTenant currentTenant)
    {
        _authorizationService = authorizationService;
        _accountRepository = accountRepository;
        _tenantRepository = tenantRepository;
        _tenantApprovalRequestRepository = tenantApprovalRequestRepository;
        _membershipRepository = membershipRepository;
        _departmentRepository = departmentRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _currentTenant = currentTenant;
    }

    public async Task<Result<AuthResponse>> Handle(CreateSharedTenantCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        
        var actorCheck = await _authorizationService.EnsureCanCreateTenantAsync(request.AccountId, request.CurrentMembershipId, cancellationToken);
        if (actorCheck.IsFailure)
            return Result.Failure<AuthResponse>(actorCheck.Error);

        var account = await _accountRepository.GetLoginInfoByIdAsync(request.AccountId, cancellationToken);
        if (account == null || !account.IsActive)
            return Result.Failure<AuthResponse>(AccountErrors.Unauthorized);

        if (await _membershipRepository.ExistsOwnerByAccountIdAsync(request.AccountId, cancellationToken))
            return Result.Failure<AuthResponse>(TenantErrors.UserAlreadyHasTenant);

        if (await _tenantRepository.ExistsByCodeAsync(request.TenantCode, cancellationToken))
            return Result.Failure<AuthResponse>(TenantErrors.CodeAlreadyExists);

        if (await _tenantApprovalRequestRepository.IsTenantCodeBlockedAsync(request.TenantCode, DateTime.UtcNow, cancellationToken))
            return Result.Failure<AuthResponse>(TenantErrors.CodeBlocked);

        var tenantResult = TenantEntity.Create(request.Name, request.TenantCode, TenancyModel.Shared, request.Currency);
        if (tenantResult.IsFailure)
            return Result.Failure<AuthResponse>(tenantResult.Error);

        var tenant = tenantResult.Value;

        var departmentResult = Department.Create("Root", tenant.Id);
        if (departmentResult.IsFailure)
            return Result.Failure<AuthResponse>(departmentResult.Error);

        var department = departmentResult.Value;

        var membershipResult = TenantMembership.Create(account.Id, tenant.Id, RoleType.TenantAdmin, isOwner: true);
        if (membershipResult.IsFailure)
            return Result.Failure<AuthResponse>(membershipResult.Error);

        var membership = membershipResult.Value;

        var refreshTokenRaw = _tokenService.GenerateRefreshToken();
        var refreshTokenResult = RefreshToken.Create(refreshTokenRaw, account.Id, membership.Id, _tokenService.RefreshTokenExpirationDays);
        if (refreshTokenResult.IsFailure)
            return Result.Failure<AuthResponse>(refreshTokenResult.Error);

        // Use AsyncLocal-backed scope to act as the new tenant during SaveChanges.
        // The tenant being created is in `provisionedTenantIds` so SaveChanges allows it.
        using (_currentTenant.BeginScope(tenant.Id, membership.Id))
        {
            _tenantRepository.Add(tenant);
            _departmentRepository.Add(department);
            _membershipRepository.Add(membership);
            _refreshTokenRepository.Add(refreshTokenResult.Value);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var accessToken = _tokenService.GenerateAccessToken(
            account.Id,
            account.Email,
            membership.Role.ToString(),
            membership.IdTenant,
            membership.Id);

        return Result.Success(new AuthResponse(
            accessToken,
            refreshTokenRaw,
            account.Id,
            membership.Id,
            account.Email,
            membership.Role,
            membership.IdTenant));
    }
}
