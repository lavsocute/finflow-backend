using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.EmailChallenges;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Application.Auth.Commands.VerifyEmailByOtp;

public sealed class VerifyEmailByOtpCommandHandler : MediatR.IRequestHandler<VerifyEmailByOtpCommand, Result>
{
    private readonly IAccountRepository _accountRepository;
    private readonly IEmailChallengeRepository _emailChallengeRepository;
    private readonly IEmailChallengeSecretService _secretService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IOtpOperationLockService _otpLockService;

    public VerifyEmailByOtpCommandHandler(
        IAccountRepository accountRepository,
        IEmailChallengeRepository emailChallengeRepository,
        IEmailChallengeSecretService secretService,
        IUnitOfWork unitOfWork,
        IClock clock,
        IOtpOperationLockService otpLockService)
    {
        _accountRepository = accountRepository;
        _emailChallengeRepository = emailChallengeRepository;
        _secretService = secretService;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _otpLockService = otpLockService;
    }

    public async Task<Result> Handle(VerifyEmailByOtpCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var accountInfo = await _accountRepository.GetLoginInfoByEmailAsync(request.Email, cancellationToken);
        if (accountInfo is null)
            return Result.Failure(AccountErrors.NotFound);

        await using var lockHandle = await _otpLockService.AcquireLockAsync(
            $"verify-email-otp:{accountInfo.Id}",
            TimeSpan.FromSeconds(30),
            cancellationToken);

        if (lockHandle == null)
            return Result.Failure(EmailChallengeErrors.InvalidOtp);

        var account = await _accountRepository.GetByIdForUpdateAsync(accountInfo.Id, cancellationToken);
        if (account is null)
            return Result.Failure(AccountErrors.NotFound);

        var challenge = await _emailChallengeRepository.GetLatestByAccountIdAndPurposeForUpdateAsync(
            account.Id,
            EmailChallengePurpose.VerifyEmail,
            cancellationToken);

        if (challenge is null)
            return Result.Failure(EmailChallengeErrors.InvalidOtp);

        var nowUtc = _clock.UtcNow;
        if (challenge.IsConsumed || challenge.IsRevoked || challenge.IsExpiredAt(nowUtc))
            return Result.Failure(challenge.IsConsumed
                ? EmailChallengeErrors.AlreadyConsumed
                : challenge.IsRevoked
                    ? EmailChallengeErrors.AlreadyRevoked
                    : EmailChallengeErrors.Expired);

        var expectedOtpHash = _secretService.HashChallengeOtp(request.Otp);
        if (!string.Equals(challenge.OtpHash, expectedOtpHash, StringComparison.OrdinalIgnoreCase))
        {
            var failedAttemptResult = challenge.RegisterFailedOtpAttempt(nowUtc);
            if (failedAttemptResult.IsFailure)
                return Result.Failure(failedAttemptResult.Error);

            _emailChallengeRepository.Update(challenge);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure(EmailChallengeErrors.InvalidOtp);
        }

        var consumeResult = challenge.Consume(nowUtc);
        if (consumeResult.IsFailure)
            return Result.Failure(consumeResult.Error);

        var markVerifiedResult = account.MarkEmailVerified(nowUtc);
        if (markVerifiedResult.IsFailure)
            return Result.Failure(markVerifiedResult.Error);

        _emailChallengeRepository.Update(challenge);
        _accountRepository.Update(account);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
