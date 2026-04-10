using FinFlow.Domain.Enums;

namespace FinFlow.Application.Auth.DTOs.Responses;

public sealed record WorkspaceSessionResponse(
    string AccessToken,
    string RefreshToken,
    Guid AccountId,
    Guid MembershipId,
    string Email,
    RoleType Role,
    Guid TenantId,
    string SessionKind);
