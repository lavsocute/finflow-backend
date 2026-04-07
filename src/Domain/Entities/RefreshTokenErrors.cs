using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public static class RefreshTokenErrors
{
    public static readonly Error NotFound = new("RefreshToken.NotFound", "The refresh token was not found");
    public static readonly Error Expired = new("RefreshToken.Expired", "The refresh token has expired");
    public static readonly Error Revoked = new("RefreshToken.Revoked", "The refresh token has been revoked");
    public static readonly Error AlreadyRevoked = new("RefreshToken.AlreadyRevoked", "The refresh token is already revoked");
}
