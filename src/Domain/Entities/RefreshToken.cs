using System.Security.Cryptography;
using System.Text;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public sealed class RefreshToken : Entity
{
    // ... existing code ...

    public static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    public static Result<RefreshToken> Create(string token, Guid accountId, Guid membershipId, int expirationDays)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Result.Failure<RefreshToken>(new Error("RefreshToken.Invalid", "Token cannot be empty"));
            
        var hashedToken = HashToken(token);
        if (accountId == Guid.Empty)
            return Result.Failure<RefreshToken>(new Error("RefreshToken.Invalid", "Account ID cannot be empty"));
        if (membershipId == Guid.Empty)
            return Result.Failure<RefreshToken>(new Error("RefreshToken.Invalid", "Membership ID cannot be empty"));
        if (expirationDays <= 0)
            return Result.Failure<RefreshToken>(new Error("RefreshToken.Invalid", "Expiration days must be positive"));

        return Result.Success(new RefreshToken(
            Guid.NewGuid(),
            hashedToken,
            accountId,
            membershipId,
            DateTime.UtcNow.AddDays(expirationDays)));
    }

    public Result Revoke(string reason = "Manual logout")
    {
        if (IsRevoked)
            return Result.Failure(new Error("RefreshToken.AlreadyRevoked", "Token is already revoked"));

        IsRevoked = true;
        ReasonRevoked = reason;
        return Result.Success();
    }

    // Trả về (Entity mới, Raw Token của entity mới) để Service trả về cho client
    public Result<(RefreshToken NewToken, string RawToken)> ReplaceWith(string newToken, int expirationDays)
    {
        if (IsRevoked)
            return Result.Failure<(RefreshToken, string)>(new Error("RefreshToken.AlreadyRevoked", "Cannot replace a revoked token"));

        // Hash token cũ đã được thay thế bằng token mới
        ReplacedByToken = HashToken(newToken);
        
        // Tạo entity mới (sẽ tự hash token bên trong)
        var createResult = Create(newToken, AccountId, MembershipId, expirationDays);
        if (createResult.IsFailure)
            return Result.Failure<(RefreshToken, string)>(createResult.Error);

        IsRevoked = true;
        ReasonRevoked = "Replaced";

        // Trả về entity mới (đã lưu hash) và raw token (để trả về client)
        return Result.Success((createResult.Value, newToken));
    }

    private RefreshToken(Guid id, string token, Guid accountId, Guid membershipId, DateTime expiresAt)
    {
        Id = id;
        Token = token;
        AccountId = accountId;
        MembershipId = membershipId;
        ExpiresAt = expiresAt;
        CreatedAt = DateTime.UtcNow;
        IsRevoked = false;
    }

    private RefreshToken() { }

    public string Token { get; private set; } = null!;
    public Guid AccountId { get; private set; }
    public Guid MembershipId { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public bool IsRevoked { get; private set; }
    public string? ReplacedByToken { get; private set; }
    public string? ReasonRevoked { get; private set; }

    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    public bool IsActive => !IsRevoked && !IsExpired;
}
