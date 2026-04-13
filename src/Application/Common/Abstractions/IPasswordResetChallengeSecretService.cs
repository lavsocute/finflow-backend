namespace FinFlow.Application.Common.Abstractions;

public interface IPasswordResetChallengeSecretService
{
    string GenerateToken();
    string GenerateOtp(int length);
    string HashToken(string token);
    string HashOtp(string otp);
}
