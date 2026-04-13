using FinFlow.Application.Auth.Support;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Entities;
using FinFlow.Domain.PasswordResetChallenges;
using FinFlow.Domain.RefreshTokens;

namespace FinFlow.Application.Auth.Commands.ResetPasswordByOtp;

public sealed class ResetPasswordByOtpCommandHandler : MediatR.IRequestHandler<ResetPasswordByOtpCommand, Result>
{
    private readonly IPasswordResetChallengeRepository _challengeRepository;
    private readonly IPasswordResetChallengeSecretService _secretService;
    private readonly IAccountRepository _accountRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUnitOfWork _unitOfWork;

    public ResetPasswordByOtpCommandHandler(
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

    public async Task<Result> Handle(ResetPasswordByOtpCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            return Result.Failure(AccountErrors.PasswordTooShort);
        if (!PasswordRules.IsStrong(request.NewPassword))
            return Result.Failure(AccountErrors.PasswordTooWeak);

        var accountInfo = await _accountRepository.GetLoginInfoByEmailAsync(request.Email, cancellationToken);
        if (accountInfo is null || !accountInfo.IsActive)
            return Result.Failure(PasswordResetChallengeErrors.InvalidOtp);

        var challenge = await _challengeRepository.GetLatestByAccountIdForUpdateAsync(accountInfo.Id, cancellationToken);
        if (challenge is null)
            return Result.Failure(PasswordResetChallengeErrors.InvalidOtp);

        var canConsume = challenge.EnsureCanBeConsumed(DateTime.UtcNow);
        if (canConsume.IsFailure)
            return canConsume;

        var otpHash = _secretService.HashOtp(request.Otp);
        if (!string.Equals(challenge.OtpHash, otpHash, StringComparison.Ordinal))
        {
            var failedAttemptResult = challenge.RegisterFailedOtpAttempt(DateTime.UtcNow);
            if (failedAttemptResult.IsFailure)
                return failedAttemptResult;

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure(PasswordResetChallengeErrors.InvalidOtp);
        }

        var account = await _accountRepository.GetByIdForUpdateAsync(accountInfo.Id, cancellationToken);
        if (account is null)
            return Result.Failure(AccountErrors.NotFound);

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
