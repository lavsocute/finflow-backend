using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Tenants;
using FinFlow.Domain.RefreshTokens;
using RefreshTokenEntity = FinFlow.Domain.Entities.RefreshToken;

namespace FinFlow.Application.Auth.Commands.Login;

public sealed class LoginCommandHandler : MediatR.IRequestHandler<LoginCommand, Result<AuthResponse>>
{
    private readonly IAccountRepository _accountRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILoginRateLimiter _rateLimiter;

    public LoginCommandHandler(
        IAccountRepository accountRepository,
        ITenantRepository tenantRepository,
        ITenantMembershipRepository membershipRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        IPasswordHasher passwordHasher,
        ILoginRateLimiter rateLimiter)
    {
        _accountRepository = accountRepository;
        _tenantRepository = tenantRepository;
        _membershipRepository = membershipRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _rateLimiter = rateLimiter;
    }

    public async Task<Result<AuthResponse>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        
        var tenant = await _tenantRepository.GetByCodeAsync(request.TenantCode.Trim(), cancellationToken);
        var tenantId = tenant?.Id;

        if (await _rateLimiter.IsBlockedAsync(request.ClientIp, request.Email, tenantId))
            return Result.Failure<AuthResponse>(AccountErrors.TooManyRequests);

        if (tenant == null || !tenant.IsActive)
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email, tenantId);
            return Result.Failure<AuthResponse>(AccountErrors.InvalidCurrentPassword);
        }

        var account = await _accountRepository.GetLoginInfoByEmailAsync(request.Email, cancellationToken);
        if (account == null || !_passwordHasher.VerifyPassword(request.Password, account.PasswordHash))
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email, tenant.Id);
            return Result.Failure<AuthResponse>(AccountErrors.InvalidCurrentPassword);
        }

        await _rateLimiter.ResetAccountAsync(request.Email, tenant.Id);

        if (!account.IsActive)
            return Result.Failure<AuthResponse>(AccountErrors.AlreadyDeactivated);

        var membership = await _membershipRepository.GetActiveByAccountAndTenantAsync(account.Id, tenant.Id, cancellationToken);
        if (membership == null)
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email, tenant.Id);
            return Result.Failure<AuthResponse>(AccountErrors.InvalidCurrentPassword);
        }

        var accessToken = _tokenService.GenerateAccessToken(
            account.Id,
            account.Email,
            membership.Role.ToString(),
            membership.IdTenant,
            membership.Id);
        var refreshTokenRaw = _tokenService.GenerateRefreshToken();
        var refreshTokenResult = RefreshTokenEntity.Create(
            refreshTokenRaw,
            account.Id,
            membership.Id,
            _tokenService.RefreshTokenExpirationDays);

        if (refreshTokenResult.IsFailure)
            return Result.Failure<AuthResponse>(refreshTokenResult.Error);

        _refreshTokenRepository.Add(refreshTokenResult.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

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
