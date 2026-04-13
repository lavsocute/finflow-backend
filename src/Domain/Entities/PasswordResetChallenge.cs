using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public sealed class PasswordResetChallenge : Entity
{
    private PasswordResetChallenge(
        Guid id,
        Guid accountId,
        string tokenHash,
        string otpHash,
        DateTime expiresAt,
        DateTime createdAt,
        DateTime lastSentAt,
        int cooldownSeconds,
        int maxOtpAttempts)
    {
        Id = id;
        AccountId = accountId;
        TokenHash = tokenHash;
        OtpHash = otpHash;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
        LastSentAt = lastSentAt;
        CooldownSeconds = cooldownSeconds;
        MaxOtpAttempts = maxOtpAttempts;
    }

    private PasswordResetChallenge()
    {
    }

    public Guid AccountId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public string OtpHash { get; private set; } = null!;
    public DateTime ExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime LastSentAt { get; private set; }
    public DateTime? ConsumedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? ReasonRevoked { get; private set; }
    public int OtpAttemptCount { get; private set; }
    public int MaxOtpAttempts { get; private set; }
    public int CooldownSeconds { get; private set; }

    public DateTime CanResendAt => LastSentAt.AddSeconds(CooldownSeconds);
    public bool IsConsumed => ConsumedAt.HasValue;
    public bool IsRevoked => RevokedAt.HasValue;

    public static Result<PasswordResetChallenge> Create(
        Guid accountId,
        string tokenHash,
        string otpHash,
        DateTime expiresAt,
        DateTime createdAt,
        DateTime lastSentAt,
        int cooldownSeconds,
        int maxOtpAttempts)
    {
        if (accountId == Guid.Empty)
            return Result.Failure<PasswordResetChallenge>(PasswordResetChallengeErrors.AccountRequired);
        if (string.IsNullOrWhiteSpace(tokenHash))
            return Result.Failure<PasswordResetChallenge>(PasswordResetChallengeErrors.TokenRequired);
        if (string.IsNullOrWhiteSpace(otpHash))
            return Result.Failure<PasswordResetChallenge>(PasswordResetChallengeErrors.OtpRequired);
        if (expiresAt <= createdAt)
            return Result.Failure<PasswordResetChallenge>(PasswordResetChallengeErrors.ExpirationRequired);
        if (cooldownSeconds < 0)
            return Result.Failure<PasswordResetChallenge>(PasswordResetChallengeErrors.InvalidCooldown);
        if (maxOtpAttempts <= 0)
            return Result.Failure<PasswordResetChallenge>(PasswordResetChallengeErrors.InvalidMaxOtpAttempts);

        return Result.Success(new PasswordResetChallenge(
            Guid.NewGuid(),
            accountId,
            tokenHash,
            otpHash,
            DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(createdAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(lastSentAt, DateTimeKind.Utc),
            cooldownSeconds,
            maxOtpAttempts));
    }

    public Result EnsureCanBeConsumed(DateTime asOfUtc)
    {
        if (IsRevoked)
            return Result.Failure(PasswordResetChallengeErrors.AlreadyRevoked);
        if (IsConsumed)
            return Result.Failure(PasswordResetChallengeErrors.AlreadyConsumed);
        if (DateTime.SpecifyKind(asOfUtc, DateTimeKind.Utc) > ExpiresAt)
            return Result.Failure(PasswordResetChallengeErrors.Expired);
        if (OtpAttemptCount >= MaxOtpAttempts)
            return Result.Failure(PasswordResetChallengeErrors.TooManyAttempts);

        return Result.Success();
    }

    public Result RegisterFailedOtpAttempt(DateTime failedAtUtc)
    {
        var canConsume = EnsureCanBeConsumed(failedAtUtc);
        if (canConsume.IsFailure)
            return canConsume;

        OtpAttemptCount++;
        if (OtpAttemptCount >= MaxOtpAttempts)
        {
            RevokedAt = DateTime.SpecifyKind(failedAtUtc, DateTimeKind.Utc);
            ReasonRevoked = "Too many invalid OTP attempts";
        }

        return Result.Success();
    }

    public Result Consume(DateTime consumedAtUtc)
    {
        var canConsume = EnsureCanBeConsumed(consumedAtUtc);
        if (canConsume.IsFailure)
            return canConsume;

        ConsumedAt = DateTime.SpecifyKind(consumedAtUtc, DateTimeKind.Utc);
        return Result.Success();
    }

    public Result Revoke(string reason, DateTime revokedAtUtc)
    {
        if (IsRevoked)
            return Result.Failure(PasswordResetChallengeErrors.AlreadyRevoked);

        RevokedAt = DateTime.SpecifyKind(revokedAtUtc, DateTimeKind.Utc);
        ReasonRevoked = reason;
        return Result.Success();
    }
}
