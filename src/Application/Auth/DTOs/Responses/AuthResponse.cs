using FinFlow.Domain.Enums;

namespace FinFlow.Application.Auth.DTOs.Responses;

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    Guid Id,
    Guid MembershipId,
    string Email,
    RoleType Role,
    Guid IdTenant
);
