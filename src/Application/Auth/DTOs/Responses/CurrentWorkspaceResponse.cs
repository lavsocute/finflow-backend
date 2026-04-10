using FinFlow.Domain.Enums;

namespace FinFlow.Application.Auth.DTOs.Responses;

public record CurrentWorkspaceResponse(
    Guid AccountId,
    string Email,
    Guid MembershipId,
    RoleType Role,
    Guid TenantId,
    string TenantCode,
    string TenantName
);
