using FinFlow.Domain.Enums;

namespace FinFlow.Application.Auth.Dtos;

public record LoginRequest(string Email, string Password, string TenantCode);
public record RegisterRequest(string Email, string Password, string Name, string TenantCode, string DepartmentName = "Root");
public record CreateSharedTenantRequest(Guid AccountId, Guid? CurrentMembershipId, string Name, string TenantCode, string Currency = "VND");
public record CompanyInfoRequest(
    string CompanyName,
    string TaxCode,
    string? Address = null,
    string? Phone = null,
    string? ContactPerson = null,
    string? BusinessType = null,
    int? EmployeeCount = null);
public record CreateIsolatedTenantRequest(
    Guid AccountId,
    Guid? CurrentMembershipId,
    string Name,
    string TenantCode,
    string Currency,
    CompanyInfoRequest CompanyInfo);
public record RefreshTokenRequest(string RefreshToken);
public record SwitchWorkspaceRequest(Guid AccountId, Guid MembershipId, string CurrentRefreshToken);
public record InviteMemberRequest(Guid InviterAccountId, Guid InviterMembershipId, string Email, RoleType Role);
public record AcceptInviteRequest(string InviteToken, string Password);
public record ChangePasswordRequest(Guid AccountId, string CurrentPassword, string NewPassword);
public record TenantApprovalResponse(Guid RequestId, TenantApprovalStatus Status, string Message, DateTime ExpiresAt);
public record PendingTenantApprovalResponse(
    Guid RequestId,
    string TenantCode,
    string Name,
    string CompanyName,
    string TaxCode,
    string? RequestedByEmail,
    int? EmployeeCount,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    TenantApprovalStatus Status);
public record TenantApprovalDecisionResponse(
    Guid RequestId,
    TenantApprovalStatus Status,
    Guid? TenantId,
    string? TenantCode,
    string? Name);
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
