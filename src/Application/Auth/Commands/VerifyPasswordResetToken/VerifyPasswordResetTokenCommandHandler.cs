using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.PasswordResetChallenges;

namespace FinFlow.Application.Auth.Commands.VerifyPasswordResetToken;

public sealed class VerifyPasswordResetTokenCommandHandler : MediatR.IRequestHandler<VerifyPasswordResetTokenCommand, Result<bool>>
{
    private readonly IPasswordResetChallengeRepository _challengeRepository;
    private readonly IPasswordResetChallengeSecretService _secretService;

    public VerifyPasswordResetTokenCommandHandler(
        IPasswordResetChallengeRepository challengeRepository,
        IPasswordResetChallengeSecretService secretService)
    {
        _challengeRepository = challengeRepository;
        _secretService = secretService;
    }

    public async Task<Result<bool>> Handle(VerifyPasswordResetTokenCommand command, CancellationToken cancellationToken)
    {
        var tokenHash = _secretService.HashToken(command.Token);
        var challenge = await _challengeRepository.GetByTokenHashAsync(tokenHash, cancellationToken);
        if (challenge is null)
            return Result.Failure<bool>(PasswordResetChallengeErrors.InvalidToken);

        var validationResult = challenge.EnsureCanBeConsumed(DateTime.UtcNow);
        if (validationResult.IsFailure)
            return Result.Failure<bool>(validationResult.Error);

        return Result.Success(true);
    }
}
