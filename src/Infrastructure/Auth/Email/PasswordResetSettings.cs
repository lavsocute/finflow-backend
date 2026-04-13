using FinFlow.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace FinFlow.Infrastructure.Auth.Email;

internal sealed class PasswordResetSettings : IPasswordResetSettings
{
    private readonly PasswordResetOptions _options;

    public PasswordResetSettings(IOptions<PasswordResetOptions> options)
    {
        _options = options.Value;
    }

    public int TokenLifetimeMinutes => _options.TokenLifetimeMinutes;
    public int CooldownSeconds => _options.CooldownSeconds;
    public int OtpLength => _options.OtpLength;
    public int TokenByteLength => _options.TokenByteLength;
    public int MaxOtpAttempts => _options.MaxOtpAttempts;
    public string ResetLinkBaseUrl => _options.ResetLinkBaseUrl;
}
