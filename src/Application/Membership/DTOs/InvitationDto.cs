using FinFlow.Domain.Enums;

namespace FinFlow.Application.Membership.DTOs;

public sealed record InvitationDto(
    Guid Id,
    string Email,
    Guid TenantId,
    RoleType Role,
    DateTime ExpiresAt,
    DateTime CreatedAt,
    DateTime? AcceptedAt,
    DateTime? RevokedAt,
    Guid? RevokedByMembershipId,
    bool IsPending,
    bool IsExpired);
