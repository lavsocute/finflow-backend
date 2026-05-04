using FinFlow.Domain.Enums;

namespace FinFlow.Application.Membership.DTOs;

public sealed record MemberDto(
    Guid Id,
    Guid AccountId,
    Guid TenantId,
    Guid? DepartmentId,
    string? FullName,
    string? Email,
    string? DepartmentName,
    RoleType Role,
    bool IsOwner,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastActiveAt,
    DateTime? DeactivatedAt,
    Guid? DeactivatedBy,
    string? DeactivatedReason);