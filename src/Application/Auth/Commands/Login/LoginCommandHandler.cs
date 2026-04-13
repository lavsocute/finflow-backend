using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.RefreshTokens;
using RefreshTokenEntity = FinFlow.Domain.Entities.RefreshToken;

namespace FinFlow.Application.Auth.Commands.Login;

public sealed class LoginCommandHandler : MediatR.IRequestHandler<LoginCommand, Result<AccountSessionResponse>>
{
    private readonly IAccountRepository _accountRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILoginRateLimiter _rateLimiter;

    public LoginCommandHandler(
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

    public async Task<Result<AccountSessionResponse>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        if (await _rateLimiter.IsBlockedAsync(request.ClientIp, request.Email))
            return Result.Failure<AccountSessionResponse>(AccountErrors.TooManyRequests);

        var account = await _accountRepository.GetLoginInfoByEmailAsync(request.Email, cancellationToken);
        if (account == null || !_passwordHasher.VerifyPassword(request.Password, account.PasswordHash))
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email);
            return Result.Failure<AccountSessionResponse>(AccountErrors.InvalidCurrentPassword);
        }

        await _rateLimiter.ResetAccountAsync(request.Email);

        if (!account.IsActive)
            return Result.Failure<AccountSessionResponse>(AccountErrors.AlreadyDeactivated);
        if (!account.IsEmailVerified)
            return Result.Failure<AccountSessionResponse>(AccountErrors.EmailNotVerified);

        var accessToken = _tokenService.GenerateAccountAccessToken(account.Id, account.Email);
        var refreshTokenRaw = _tokenService.GenerateRefreshToken();
        var refreshTokenResult = RefreshTokenEntity.CreateAccountSession(
            refreshTokenRaw,
            account.Id,
            _tokenService.RefreshTokenExpirationDays);

        if (refreshTokenResult.IsFailure)
            return Result.Failure<AccountSessionResponse>(refreshTokenResult.Error);

        _refreshTokenRepository.Add(refreshTokenResult.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new AccountSessionResponse(
            accessToken,
            refreshTokenRaw,
            account.Id,
            account.Email));
    }
}
