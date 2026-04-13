using System.Security.Cryptography;
using System.Text;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Infrastructure.Auth.Email;
using Microsoft.Extensions.Options;

namespace FinFlow.Infrastructure.Auth;

internal sealed class PasswordResetChallengeSecretService : IPasswordResetChallengeSecretService
{
    private readonly PasswordResetOptions _options;

    public PasswordResetChallengeSecretService(IOptions<PasswordResetOptions> options)
    {
        _options = options.Value;
    }

    public string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(_options.TokenByteLength);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public string GenerateOtp(int length)
    {
        var digits = new char[length];
        for (var index = 0; index < length; index++)
        {
            digits[index] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        }

        return new string(digits);
    }

    public string HashToken(string token) => Hash(token);

    public string HashOtp(string otp) => Hash(otp);

    private string Hash(string value)
    {
        var key = Encoding.UTF8.GetBytes(_options.TokenHashKey);
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }
}
