using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.EmailChallenges;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Application.Auth.Commands.VerifyEmailByToken;

public sealed class VerifyEmailByTokenCommandHandler : MediatR.IRequestHandler<VerifyEmailByTokenCommand, Result>
{
    private readonly IEmailChallengeRepository _emailChallengeRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailChallengeSecretService _secretService;
    private readonly IClock _clock;
    private readonly IOtpOperationLockService _otpLockService;

    public VerifyEmailByTokenCommandHandler(
        IEmailChallengeRepository emailChallengeRepository,
        IAccountRepository accountRepository,
        IUnitOfWork unitOfWork,
        IEmailChallengeSecretService secretService,
        IClock clock,
        IOtpOperationLockService otpLockService)
    {
        _emailChallengeRepository = emailChallengeRepository;
        _accountRepository = accountRepository;
        _unitOfWork = unitOfWork;
        _secretService = secretService;
        _clock = clock;
        _otpLockService = otpLockService;
    }

    public async Task<Result> Handle(VerifyEmailByTokenCommand command, CancellationToken cancellationToken)
    {
        var tokenHash = _secretService.HashChallengeToken(command.Request.Token);
        var challenge = await _emailChallengeRepository.GetByTokenHashForUpdateAsync(tokenHash, cancellationToken);
        if (challenge is null)
            return Result.Failure(EmailChallengeErrors.InvalidToken);

        await using var lockHandle = await _otpLockService.AcquireLockAsync(
            $"verify-email-token:{challenge.Id}",
            TimeSpan.FromSeconds(30),
            cancellationToken);

        if (lockHandle == null)
            return Result.Failure(EmailChallengeErrors.InvalidToken);

        var nowUtc = _clock.UtcNow;
        var consumeResult = challenge.Consume(nowUtc);
        if (consumeResult.IsFailure)
            return Result.Failure(consumeResult.Error);

        var account = await _accountRepository.GetByIdForUpdateAsync(challenge.AccountId, cancellationToken);
        if (account is null)
            return Result.Failure(AccountErrors.NotFound);

        var markVerifiedResult = account.MarkEmailVerified(nowUtc);
        if (markVerifiedResult.IsFailure)
            return Result.Failure(markVerifiedResult.Error);

        _emailChallengeRepository.Update(challenge);
        _accountRepository.Update(account);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
