using FinFlow.Application.Membership.DTOs;
using FinFlow.Domain.Enums;

namespace FinFlow.Api.GraphQL.Membership;

public sealed class MemberType
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? DepartmentName { get; set; }
    public RoleType Role { get; set; }
    public bool IsOwner { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastActiveAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }
    public Guid? DeactivatedBy { get; set; }
    public string? DeactivatedReason { get; set; }

    public static MemberType FromDto(MemberDto dto) => new()
    {
        Id = dto.Id,
        AccountId = dto.AccountId,
        TenantId = dto.TenantId,
        DepartmentId = dto.DepartmentId,
        FullName = dto.FullName,
        Email = dto.Email,
        DepartmentName = dto.DepartmentName,
        Role = dto.Role,
        IsOwner = dto.IsOwner,
        IsActive = dto.IsActive,
        CreatedAt = dto.CreatedAt,
        LastActiveAt = dto.LastActiveAt,
        DeactivatedAt = dto.DeactivatedAt,
        DeactivatedBy = dto.DeactivatedBy,
        DeactivatedReason = dto.DeactivatedReason
    };
}

public sealed class InvitationType
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public Guid TenantId { get; set; }
    public RoleType Role { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? RevokedByMembershipId { get; set; }
    public bool IsPending { get; set; }
    public bool IsExpired { get; set; }

    public static InvitationType FromDto(InvitationDto dto) => new()
    {
        Id = dto.Id,
        Email = dto.Email,
        TenantId = dto.TenantId,
        Role = dto.Role,
        ExpiresAt = dto.ExpiresAt,
        CreatedAt = dto.CreatedAt,
        AcceptedAt = dto.AcceptedAt,
        RevokedAt = dto.RevokedAt,
        RevokedByMembershipId = dto.RevokedByMembershipId,
        IsPending = dto.IsPending,
        IsExpired = dto.IsExpired
    };
}