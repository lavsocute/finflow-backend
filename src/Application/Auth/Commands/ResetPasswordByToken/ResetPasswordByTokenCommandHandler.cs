using FinFlow.Application.Auth.Support;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Entities;
using FinFlow.Domain.PasswordResetChallenges;
using FinFlow.Domain.RefreshTokens;

namespace FinFlow.Application.Auth.Commands.ResetPasswordByToken;

public sealed class ResetPasswordByTokenCommandHandler : MediatR.IRequestHandler<ResetPasswordByTokenCommand, Result>
{
    private readonly IPasswordResetChallengeRepository _challengeRepository;
    private readonly IPasswordResetChallengeSecretService _secretService;
    private readonly IAccountRepository _accountRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUnitOfWork _unitOfWork;

    public ResetPasswordByTokenCommandHandler(
        IPasswordResetChallengeRepository challengeRepository,
        IPasswordResetChallengeSecretService secretService,
        IAccountRepository accountRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IPasswordHasher passwordHasher,
        IUnitOfWork unitOfWork)
    {
        _challengeRepository = challengeRepository;
        _secretService = secretService;
        _accountRepository = accountRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _passwordHasher = passwordHasher;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ResetPasswordByTokenCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            return Result.Failure(AccountErrors.PasswordTooShort);
        if (!PasswordRules.IsStrong(request.NewPassword))
            return Result.Failure(AccountErrors.PasswordTooWeak);

        var tokenHash = _secretService.HashToken(request.Token);
        var challenge = await _challengeRepository.GetByTokenHashForUpdateAsync(tokenHash, cancellationToken);
        if (challenge is null)
            return Result.Failure(PasswordResetChallengeErrors.InvalidToken);

        var validationResult = challenge.EnsureCanBeConsumed(DateTime.UtcNow);
        if (validationResult.IsFailure)
            return validationResult;

        var account = await _accountRepository.GetByIdForUpdateAsync(challenge.AccountId, cancellationToken);
        if (account is null)
            return Result.Failure(AccountErrors.NotFound);
        if (!account.IsActive)
            return Result.Failure(AccountErrors.AlreadyDeactivated);

        var changePasswordResult = account.ChangePassword(_passwordHasher.HashPassword(request.NewPassword));
        if (changePasswordResult.IsFailure)
            return changePasswordResult;

        var consumeResult = challenge.Consume(DateTime.UtcNow);
        if (consumeResult.IsFailure)
            return consumeResult;

        await _refreshTokenRepository.RevokeAllForAccountAsync(account.Id, "Password reset", cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
