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

namespace FinFlow.Application.Auth.Commands.Register;

public sealed class RegisterCommandHandler : MediatR.IRequestHandler<RegisterCommand, Result<RegistrationPendingResponse>>
{
    private const string VerificationLinkQueryKey = "token";

    private readonly IAccountRepository _accountRepository;
    private readonly IEmailChallengeRepository _emailChallengeRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILoginRateLimiter _rateLimiter;
    private readonly IEmailChallengeSecretService _secretService;
    private readonly IEmailSender _emailSender;
    private readonly IClock _clock;
    private readonly IRegistrationChallengeSettings _challengeSettings;

    public RegisterCommandHandler(
        IAccountRepository accountRepository,
        IEmailChallengeRepository emailChallengeRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ILoginRateLimiter rateLimiter,
        IEmailChallengeSecretService secretService,
        IEmailSender emailSender,
        IClock clock,
        IRegistrationChallengeSettings challengeSettings)
    {
        _accountRepository = accountRepository;
        _emailChallengeRepository = emailChallengeRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _rateLimiter = rateLimiter;
        _secretService = secretService;
        _emailSender = emailSender;
        _clock = clock;
        _challengeSettings = challengeSettings;
    }

    public async Task<Result<RegistrationPendingResponse>> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        if (await _rateLimiter.IsBlockedAsync(request.ClientIp, request.Email))
            return Result.Failure<RegistrationPendingResponse>(AccountErrors.TooManyRequests);

        if (await _accountRepository.ExistsByEmailIgnoringTenantAsync(request.Email, cancellationToken))
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email);
            return Result.Failure<RegistrationPendingResponse>(AccountErrors.EmailAlreadyExists);
        }

        if (!PasswordRules.IsStrong(request.Password))
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email);
            return Result.Failure<RegistrationPendingResponse>(AccountErrors.PasswordTooWeak);
        }

        var nowUtc = _clock.UtcNow;
        var accountResult = Account.Create(request.Email, _passwordHasher.HashPassword(request.Password), nowUtc);
        if (accountResult.IsFailure)
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email);
            return Result.Failure<RegistrationPendingResponse>(accountResult.Error);
        }

        var account = accountResult.Value;
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
        {
            await _rateLimiter.RecordFailureAsync(request.ClientIp, request.Email);
            return Result.Failure<RegistrationPendingResponse>(challengeResult.Error);
        }

        var verificationLinkResult = BuildVerificationLink(rawToken);
        if (verificationLinkResult.IsFailure)
            return Result.Failure<RegistrationPendingResponse>(verificationLinkResult.Error);

        // Persist account and challenge BEFORE sending email.
        // If email fails, user can request resend. If we send first and DB fails,
        // user receives a token that doesn't exist — unrecoverable.
        _accountRepository.Add(account);
        _emailChallengeRepository.Add(challengeResult.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await _emailSender.SendVerificationEmailAsync(account.Email, verificationLinkResult.Value, rawOtp, cancellationToken);
        }
        catch
        {
            // Account is persisted. User can request resend via ResendEmailVerification.
            // Don't fail the registration — the account exists and is valid.
        }

        await _rateLimiter.ResetAccountAsync(request.Email);

        return Result.Success(new RegistrationPendingResponse(
            account.Id,
            account.Email,
            true,
            _challengeSettings.VerificationCooldownSeconds));
    }

    private Result<string> BuildVerificationLink(string rawToken)
    {
        var baseUrl = _challengeSettings.VerificationLinkBaseUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(baseUrl))
            return Result.Failure<string>(EmailChallengeErrors.VerificationLinkBaseUrlRequired);

        // Use URL fragment (#) instead of query string (?). Fragments are NOT sent to the server,
        // so they don't appear in access logs, proxies, or referrer headers. Frontend extracts the
        // token via JavaScript (window.location.hash) and POSTs it to the verification endpoint.
        var encodedToken = WebUtility.UrlEncode(rawToken);
        return Result.Success($"{baseUrl.TrimEnd('/')}#{VerificationLinkQueryKey}={encodedToken}");
    }
}
