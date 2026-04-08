using FinFlow.Domain.Enums;

namespace FinFlow.Application.Auth.Dtos;

public record LoginRequest(string Email, string Password, string TenantCode);
public record RegisterRequest(string Email, string Password, string Name, string TenantCode, string DepartmentName = "Root");
public record RefreshTokenRequest(string RefreshToken);
public record SwitchWorkspaceRequest(Guid AccountId, Guid MembershipId, string CurrentRefreshToken);
public record InviteMemberRequest(Guid InviterAccountId, Guid InviterMembershipId, string Email, RoleType Role);
public record AcceptInviteRequest(string InviteToken, string Password);
public record ChangePasswordRequest(Guid AccountId, string CurrentPassword, string NewPassword);
public record InvitationResponse(
    Guid InvitationId,
    string InviteToken,
    string Email,
    RoleType Role,
    Guid IdTenant,
    DateTime ExpiresAt
);
public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    Guid Id,
    Guid MembershipId,
    string Email,
    RoleType Role,
    Guid IdTenant
);
