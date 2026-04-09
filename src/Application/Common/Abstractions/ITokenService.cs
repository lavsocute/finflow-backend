namespace FinFlow.Application.Common.Abstractions;

public interface ITokenService
{
    int RefreshTokenExpirationDays { get; }
    string GenerateAccessToken(Guid id, string email, string role, Guid idTenant, Guid membershipId);
    string GenerateRefreshToken();
}
