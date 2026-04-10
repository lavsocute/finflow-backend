using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Auth.Support;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.RefreshTokens;
using RefreshTokenEntity = FinFlow.Domain.Entities.RefreshToken;

namespace FinFlow.Application.Auth.Commands.Register;

public sealed class RegisterCommandHandler : MediatR.IRequestHandler<RegisterCommand, Result<AccountSessionResponse>>
{
    private readonly IAccountRepository _accountRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILoginRateLimiter _rateLimiter;

    public RegisterCommandHandler(
        IAccountRepository accountRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        IPasswordHasher passwordHasher,
        ILoginRateLimiter rateLimiter)
    {
        _accountRepository = accountRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _rateLimiter = rateLimiter;
    }

    public async Task<Result<AccountSessionResponse>> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        if (await _rateLimiter.IsBlockedAsync(request.ClientIp, request.Email))
            return Result.Failure<AccountSessionResponse>(AccountErrors.TooManyRequests);

        if (await _accountRepository.ExistsByEmailIgnoringTenantAsync(request.Email, cancellationToken))
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email);
            return Result.Failure<AccountSessionResponse>(AccountErrors.EmailAlreadyExists);
        }

        if (!PasswordRules.IsStrong(request.Password))
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email);
            return Result.Failure<AccountSessionResponse>(AccountErrors.PasswordTooWeak);
        }

        var accountResult = Account.Create(request.Email, _passwordHasher.HashPassword(request.Password));
        if (accountResult.IsFailure)
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email);
            return Result.Failure<AccountSessionResponse>(accountResult.Error);
        }

        var account = accountResult.Value;
        var refreshTokenRaw = _tokenService.GenerateRefreshToken();
        var refreshTokenResult = RefreshTokenEntity.CreateAccountSession(
            refreshTokenRaw,
            account.Id,
            _tokenService.RefreshTokenExpirationDays);

        if (refreshTokenResult.IsFailure)
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email);
            return Result.Failure<AccountSessionResponse>(refreshTokenResult.Error);
        }

        _accountRepository.Add(account);
        _refreshTokenRepository.Add(refreshTokenResult.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _rateLimiter.ResetAccountAsync(request.Email);

        var accessToken = _tokenService.GenerateAccountAccessToken(account.Id, account.Email);

        return Result.Success(new AccountSessionResponse(
            accessToken,
            refreshTokenRaw,
            account.Id,
            account.Email));
    }
}
