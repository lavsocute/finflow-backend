namespace FinFlow.Infrastructure.Auth.Email;

internal sealed class PasswordResetOptions
{
    public int TokenLifetimeMinutes { get; init; } = 15;
    public int CooldownSeconds { get; init; } = 90;
    public int OtpLength { get; init; } = 6;
    public int TokenByteLength { get; init; } = 32;
    public int MaxOtpAttempts { get; init; } = 5;
    public string TokenHashKey { get; init; } = string.Empty;
    public string ResetLinkBaseUrl { get; init; } = "http://localhost:4200/reset-password";
}
