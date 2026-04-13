namespace FinFlow.Application.Common.Abstractions;

public interface IPasswordResetSettings
{
    int TokenLifetimeMinutes { get; }
    int CooldownSeconds { get; }
    int OtpLength { get; }
    int TokenByteLength { get; }
    int MaxOtpAttempts { get; }
    string ResetLinkBaseUrl { get; }
}
