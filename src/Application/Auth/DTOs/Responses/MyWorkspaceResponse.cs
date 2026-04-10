using FinFlow.Domain.Enums;

namespace FinFlow.Application.Auth.DTOs.Responses;

public sealed record MyWorkspaceResponse(
    Guid WorkspaceId,
    Guid TenantId,
    string TenantCode,
    string TenantName,
    Guid MembershipId,
    RoleType Role);
