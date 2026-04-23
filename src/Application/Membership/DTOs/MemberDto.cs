using FinFlow.Domain.Enums;

namespace FinFlow.Application.Membership.DTOs;

public sealed record MemberDto(
    Guid Id,
    Guid AccountId,
    Guid TenantId,
    Guid? DepartmentId,
    RoleType Role,
    bool IsOwner,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? DeactivatedAt,
    Guid? DeactivatedBy,
    string? DeactivatedReason);
