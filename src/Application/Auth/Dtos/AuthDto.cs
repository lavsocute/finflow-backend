using FinFlow.Domain.Enums;

namespace FinFlow.Application.Auth.Dtos;

public record LoginRequest(string Email, string Password, string TenantCode);
public record RegisterRequest(string Email, string Password, string Name, string TenantCode, string DepartmentName = "Root");
public record RefreshTokenRequest(string RefreshToken);
public record ChangePasswordRequest(Guid AccountId, string CurrentPassword, string NewPassword);
public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    Guid Id,
    string Email,
    RoleType Role,
    Guid IdTenant,
    Guid IdDepartment
);
