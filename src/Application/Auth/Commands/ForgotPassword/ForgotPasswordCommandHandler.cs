using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Entities;
using FinFlow.Domain.PasswordResetChallenges;

namespace FinFlow.Application.Auth.Commands.ForgotPassword;

public sealed class ForgotPasswordCommandHandler : MediatR.IRequestHandler<ForgotPasswordCommand, Result<ChallengeDispatchResponse>>
{
    private readonly IAccountRepository _accountRepository;
    private readonly IPasswordResetChallengeRepository _challengeRepository;
    private readonly IPasswordResetChallengeSecretService _secretService;
    private readonly IPasswordResetSettings _settings;
    private readonly IEmailSender _emailSender;
    private readonly IUnitOfWork _unitOfWork;

    public ForgotPasswordCommandHandler(
        IAccountRepository accountRepository,
        IPasswordResetChallengeRepository challengeRepository,
        IPasswordResetChallengeSecretService secretService,
        IPasswordResetSettings settings,
        IEmailSender emailSender,
        IUnitOfWork unitOfWork)
    {
        _accountRepository = accountRepository;
        _challengeRepository = challengeRepository;
        _secretService = secretService;
        _settings = settings;
        _emailSender = emailSender;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ChallengeDispatchResponse>> Handle(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        var account = await _accountRepository.GetLoginInfoByEmailAsync(command.Request.Email, cancellationToken);
        var neutralResponse = new ChallengeDispatchResponse(true, _settings.CooldownSeconds);

        if (account is null || !account.IsActive)
        {
            return Result.Success(neutralResponse);
        }

        var nowUtc = DateTime.UtcNow;
        var latestChallenge = await _challengeRepository.GetLatestByAccountIdForUpdateAsync(account.Id, cancellationToken);
        if (latestChallenge is not null && latestChallenge.CanResendAt > nowUtc)
        {
            var remainingCooldown = (int)Math.Ceiling((latestChallenge.CanResendAt - nowUtc).TotalSeconds);
            return Result.Success(new ChallengeDispatchResponse(true, Math.Max(remainingCooldown, 0)));
        }

        latestChallenge?.Revoke("Superseded by a new password reset request", nowUtc);

        var rawToken = _secretService.GenerateToken();
        var rawOtp = _secretService.GenerateOtp(_settings.OtpLength);

        var challengeResult = PasswordResetChallenge.Create(
            account.Id,
            _secretService.HashToken(rawToken),
            _secretService.HashOtp(rawOtp),
            nowUtc.AddMinutes(_settings.TokenLifetimeMinutes),
            nowUtc,
            nowUtc,
            _settings.CooldownSeconds,
            _settings.MaxOtpAttempts);

        if (challengeResult.IsFailure)
        {
            return Result.Failure<ChallengeDispatchResponse>(challengeResult.Error);
        }

        _challengeRepository.Add(challengeResult.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var resetLink = BuildResetLink(rawToken);
            await _emailSender.SendPasswordResetEmailAsync(account.Email, resetLink, rawOtp, cancellationToken);
        }
        catch
        {
            // Keep the public response neutral to avoid account enumeration.
        }

        return Result.Success(neutralResponse);
    }

    private string BuildResetLink(string rawToken)
    {
        var baseUrl = _settings.ResetLinkBaseUrl.TrimEnd('/');
        return $"{baseUrl}?token={Uri.EscapeDataString(rawToken)}";
    }
}
