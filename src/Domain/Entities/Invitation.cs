using System.Security.Cryptography;
using System.Text;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Events;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class Invitation : Entity, IMultiTenant
{
    private Invitation(
        Guid id,
        string email,
        Guid idTenant,
        Guid invitedByMembershipId,
        RoleType role,
        string tokenHash,
        DateTime expiresAt)
    {
        Id = id;
        Email = email;
        IdTenant = idTenant;
        InvitedByMembershipId = invitedByMembershipId;
        Role = role;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedAt = DateTime.UtcNow;
    }

    private Invitation() { }

    public string Email { get; private set; } = null!;
    public Guid IdTenant { get; private set; }
    public Guid InvitedByMembershipId { get; private set; }
    public RoleType Role { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTime ExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public bool IsActive { get; private set; } = true;

    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    public bool IsPending => IsActive && !AcceptedAt.HasValue && !RevokedAt.HasValue && !IsExpired;

    public static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    public static Result<Invitation> Create(
        string email,
        Guid idTenant,
        Guid invitedByMembershipId,
        RoleType role,
        string rawToken,
        DateTime expiresAt)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalizedEmail))
            return Result.Failure<Invitation>(InvitationErrors.EmailRequired);

        if (!System.Text.RegularExpressions.Regex.IsMatch(normalizedEmail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return Result.Failure<Invitation>(InvitationErrors.InvalidEmail);

        if (idTenant == Guid.Empty)
            return Result.Failure<Invitation>(InvitationErrors.TenantRequired);

        if (invitedByMembershipId == Guid.Empty)
            return Result.Failure<Invitation>(InvitationErrors.InviterMembershipRequired);

        if (role is RoleType.SuperAdmin)
            return Result.Failure<Invitation>(InvitationErrors.InvalidRole);

        if (string.IsNullOrWhiteSpace(rawToken))
            return Result.Failure<Invitation>(InvitationErrors.TokenRequired);

        if (expiresAt <= DateTime.UtcNow)
            return Result.Failure<Invitation>(InvitationErrors.ExpirationRequired);

        var invitation = new Invitation(
            Guid.NewGuid(),
            normalizedEmail,
            idTenant,
            invitedByMembershipId,
            role,
            HashToken(rawToken),
            expiresAt);

        invitation.RaiseDomainEvent(new InvitationCreatedDomainEvent(
            invitation.Id,
            invitation.IdTenant,
            invitation.InvitedByMembershipId,
            invitation.Email,
            invitation.Role,
            invitation.ExpiresAt));

        return Result.Success(invitation);
    }

    public Result Revoke()
    {
        if (AcceptedAt.HasValue)
            return Result.Failure(InvitationErrors.AlreadyAccepted);

        if (RevokedAt.HasValue || !IsActive)
            return Result.Failure(InvitationErrors.AlreadyRevoked);

        IsActive = false;
        RevokedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result MarkAccepted()
    {
        if (AcceptedAt.HasValue)
            return Result.Failure(InvitationErrors.AlreadyAccepted);

        if (RevokedAt.HasValue || !IsActive)
            return Result.Failure(InvitationErrors.AlreadyRevoked);

        if (IsExpired)
            return Result.Failure(InvitationErrors.Expired);

        AcceptedAt = DateTime.UtcNow;
        return Result.Success();
    }
}
