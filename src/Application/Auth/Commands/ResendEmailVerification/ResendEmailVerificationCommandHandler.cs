using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Auth.Support;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.EmailChallenges;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using System.Net;

namespace FinFlow.Application.Auth.Commands.ResendEmailVerification;

public sealed class ResendEmailVerificationCommandHandler : MediatR.IRequestHandler<ResendEmailVerificationCommand, Result<ChallengeDispatchResponse>>
{
    private const string VerificationLinkQueryKey = "token";

    private readonly IAccountRepository _accountRepository;
    private readonly IEmailChallengeRepository _emailChallengeRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailChallengeSecretService _secretService;
    private readonly IEmailSender _emailSender;
    private readonly IClock _clock;
    private readonly IRegistrationChallengeSettings _challengeSettings;
    private readonly IOtpOperationLockService _otpLockService;

    public ResendEmailVerificationCommandHandler(
        IAccountRepository accountRepository,
        IEmailChallengeRepository emailChallengeRepository,
        IUnitOfWork unitOfWork,
        IEmailChallengeSecretService secretService,
        IEmailSender emailSender,
        IClock clock,
        IRegistrationChallengeSettings challengeSettings,
        IOtpOperationLockService otpLockService)
    {
        _accountRepository = accountRepository;
        _emailChallengeRepository = emailChallengeRepository;
        _unitOfWork = unitOfWork;
        _secretService = secretService;
        _emailSender = emailSender;
        _clock = clock;
        _challengeSettings = challengeSettings;
        _otpLockService = otpLockService;
    }

    public async Task<Result<ChallengeDispatchResponse>> Handle(ResendEmailVerificationCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var accountInfo = await _accountRepository.GetLoginInfoByEmailAsync(request.Email, cancellationToken);
        var cooldownSeconds = Math.Max(0, _challengeSettings.VerificationCooldownSeconds);

        if (accountInfo is null || !accountInfo.IsActive || accountInfo.IsEmailVerified)
            return Result.Success(new ChallengeDispatchResponse(true, cooldownSeconds));

        await using var lockHandle = await _otpLockService.AcquireLockAsync(
            $"resend-verify:{accountInfo.Id}",
            TimeSpan.FromSeconds(30),
            cancellationToken);

        if (lockHandle == null)
            return Result.Success(new ChallengeDispatchResponse(true, cooldownSeconds));

        var account = await _accountRepository.GetByIdForUpdateAsync(accountInfo.Id, cancellationToken);
        if (account is null)
            return Result.Success(new ChallengeDispatchResponse(true, cooldownSeconds));

        var nowUtc = _clock.UtcNow;
        var latestChallenge = await _emailChallengeRepository.GetLatestByAccountIdAndPurposeForUpdateAsync(
            account.Id,
            EmailChallengePurpose.VerifyEmail,
            cancellationToken);

        if (latestChallenge is not null && latestChallenge.IsUsableAt(nowUtc))
        {
            var resendAvailableAt = (latestChallenge.LastSentAt ?? latestChallenge.CreatedAt).AddSeconds(cooldownSeconds);
            if (resendAvailableAt > nowUtc)
                return Result.Success(new ChallengeDispatchResponse(true, cooldownSeconds));

            var revokeResult = latestChallenge.Revoke(nowUtc);
            if (revokeResult.IsFailure)
                return Result.Failure<ChallengeDispatchResponse>(revokeResult.Error);

            _emailChallengeRepository.Update(latestChallenge);
        }

        var rawToken = _secretService.GenerateVerificationToken();
        var rawOtp = _secretService.GenerateVerificationOtp();
        var tokenHash = _secretService.HashChallengeToken(rawToken);
        var otpHash = _secretService.HashChallengeOtp(rawOtp);

        var challengeResult = EmailChallenge.Create(
            account.Id,
            EmailChallengePurpose.VerifyEmail,
            nowUtc,
            nowUtc.AddMinutes(Math.Max(1, _challengeSettings.VerificationTokenLifetimeMinutes)),
            email: account.Email,
            tokenHash: tokenHash,
            otpHash: otpHash,
            lastSentAtUtc: nowUtc);

        if (challengeResult.IsFailure)
            return Result.Failure<ChallengeDispatchResponse>(challengeResult.Error);

        var verificationLinkResult = BuildVerificationLink(rawToken);
        if (verificationLinkResult.IsFailure)
            return Result.Failure<ChallengeDispatchResponse>(verificationLinkResult.Error);

        try
        {
            await _emailSender.SendVerificationEmailAsync(account.Email, verificationLinkResult.Value, rawOtp, cancellationToken);
        }
        catch
        {
            return Result.Failure<ChallengeDispatchResponse>(EmailChallengeErrors.EmailDeliveryFailed);
        }

        _emailChallengeRepository.Add(challengeResult.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new ChallengeDispatchResponse(true, cooldownSeconds));
    }

    private Result<string> BuildVerificationLink(string rawToken)
    {
        var baseUrl = _challengeSettings.VerificationLinkBaseUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(baseUrl))
            return Result.Failure<string>(EmailChallengeErrors.VerificationLinkBaseUrlRequired);

        var encodedToken = WebUtility.UrlEncode(rawToken);
        return Result.Success($"{baseUrl.TrimEnd('/')}?{VerificationLinkQueryKey}={encodedToken}");
    }
}
