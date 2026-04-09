using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Auth.Support;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.RefreshTokens;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Tenants;
using RefreshTokenEntity = FinFlow.Domain.Entities.RefreshToken;
using TenantEntity = FinFlow.Domain.Entities.Tenant;

namespace FinFlow.Application.Auth.Commands.Register;

public sealed class RegisterCommandHandler : MediatR.IRequestHandler<RegisterCommand, Result<AuthResponse>>
{
    private readonly IAccountRepository _accountRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILoginRateLimiter _rateLimiter;

    public RegisterCommandHandler(
        IAccountRepository accountRepository,
        ITenantRepository tenantRepository,
        IDepartmentRepository departmentRepository,
        ITenantMembershipRepository membershipRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        IPasswordHasher passwordHasher,
        ILoginRateLimiter rateLimiter)
    {
        _accountRepository = accountRepository;
        _tenantRepository = tenantRepository;
        _departmentRepository = departmentRepository;
        _membershipRepository = membershipRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _rateLimiter = rateLimiter;
    }

    public async Task<Result<AuthResponse>> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        
        if (await _rateLimiter.IsBlockedAsync(request.ClientIp, request.Email))
            return Result.Failure<AuthResponse>(AccountErrors.TooManyRequests);

        if (await _accountRepository.ExistsByEmailIgnoringTenantAsync(request.Email, cancellationToken))
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email);
            return Result.Failure<AuthResponse>(AccountErrors.EmailAlreadyExists);
        }

        if (await _tenantRepository.ExistsByCodeAsync(request.TenantCode, cancellationToken))
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email);
            return Result.Failure<AuthResponse>(TenantErrors.CodeAlreadyExists);
        }

        if (!PasswordRules.IsStrong(request.Password))
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email);
            return Result.Failure<AuthResponse>(AccountErrors.PasswordTooWeak);
        }

        var tenantResult = TenantEntity.Create(request.Name, request.TenantCode, TenancyModel.Shared, "VND");
        if (tenantResult.IsFailure)
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email);
            return Result.Failure<AuthResponse>(tenantResult.Error);
        }

        var tenant = tenantResult.Value;
        var departmentResult = Department.Create(request.DepartmentName, tenant.Id);
        if (departmentResult.IsFailure)
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email);
            return Result.Failure<AuthResponse>(departmentResult.Error);
        }

        var department = departmentResult.Value;
        var accountResult = Account.Create(request.Email, _passwordHasher.HashPassword(request.Password), department.Id);
        if (accountResult.IsFailure)
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email);
            return Result.Failure<AuthResponse>(accountResult.Error);
        }

        var account = accountResult.Value;
        var membershipResult = TenantMembership.Create(account.Id, tenant.Id, RoleType.TenantAdmin, isOwner: true);
        if (membershipResult.IsFailure)
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email);
            return Result.Failure<AuthResponse>(membershipResult.Error);
        }

        var membership = membershipResult.Value;
        var refreshTokenRaw = _tokenService.GenerateRefreshToken();
        var refreshTokenResult = RefreshTokenEntity.Create(
            refreshTokenRaw,
            account.Id,
            membership.Id,
            _tokenService.RefreshTokenExpirationDays);

        if (refreshTokenResult.IsFailure)
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email);
            return Result.Failure<AuthResponse>(refreshTokenResult.Error);
        }

        _tenantRepository.Add(tenant);
        _departmentRepository.Add(department);
        _accountRepository.Add(account);
        _membershipRepository.Add(membership);
        _refreshTokenRepository.Add(refreshTokenResult.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _rateLimiter.ResetAccountAsync(request.Email);

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
