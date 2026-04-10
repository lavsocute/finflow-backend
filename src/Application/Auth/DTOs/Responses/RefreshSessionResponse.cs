using FinFlow.Domain.Enums;

namespace FinFlow.Application.Auth.DTOs.Responses;

public record RefreshSessionResponse(
    string AccessToken,
    string RefreshToken,
    Guid Id,
    string Email,
    string SessionKind,
    Guid? MembershipId = null,
    RoleType? Role = null,
    Guid? IdTenant = null
);
