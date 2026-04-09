using FinFlow.Api.Extensions;
using FinFlow.Application.Auth.Dtos;
using FinFlow.Application.Auth.Interfaces;
using FinFlow.Domain.Enums;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace FinFlow.Api.GraphQL.Auth;

public record LoginInput(string Email, string Password, string TenantCode);
public record RegisterInput(string Email, string Password, string Name, string TenantCode, string DepartmentName = "Root");
public record CreateSharedTenantInput(string Name, string TenantCode, string Currency = "VND");
public record CompanyInfoInput(
    string CompanyName,
    string TaxCode,
    string? Address = null,
    string? Phone = null,
    string? ContactPerson = null,
    string? BusinessType = null,
    int? EmployeeCount = null);
public record CreateIsolatedTenantInput(string Name, string TenantCode, string Currency, CompanyInfoInput CompanyInfo);
public record RefreshTokenInput(string RefreshToken);
public record SwitchWorkspaceInput(Guid MembershipId, string CurrentRefreshToken);
public record InviteMemberInput(string Email, RoleType Role);
public record AcceptInviteInput(string InviteToken, string Password);
public record ChangePasswordInput(string CurrentPassword, string NewPassword);

public record AuthPayload(
    string AccessToken,
    string RefreshToken,
    Guid Id,
    Guid MembershipId,
    string Email,
    RoleType Role,
    Guid IdTenant
);

public record InvitationPayload(
    Guid InvitationId,
    string InviteToken,
    string Email,
    RoleType Role,
    Guid IdTenant,
    DateTime ExpiresAt
);

public record TenantApprovalPayload(
    Guid RequestId,
    string Status,
    string Message,
    DateTime ExpiresAt
);

public record TenantApprovalDecisionPayload(
    Guid RequestId,
    string Status,
    Guid? TenantId,
    string? TenantCode,
    string? Name
);

public class AuthMutations
{
    public async Task<AuthPayload> LoginAsync(
        LoginInput input,
        [Service] IAuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var clientIp = httpContextAccessor.HttpContext?.GetClientIpAddress();
        // Nếu không xác định được IP, truyền null để RateLimiter bỏ qua bước chặn theo IP
        // (tránh việc dùng Guid làm vô hiệu hóa cơ chế chặn theo IP).
        if (clientIp == "unknown") clientIp = null;
        
        var result = await authService.LoginAsync(new LoginRequest(input.Email, input.Password, input.TenantCode), clientIp, cancellationToken);
        return HandleResult(result);
    }

    public async Task<AuthPayload> RegisterAsync(
        RegisterInput input,
        [Service] IAuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var clientIp = httpContextAccessor.HttpContext?.GetClientIpAddress();
        if (clientIp == "unknown") clientIp = null;

        var result = await authService.RegisterAsync(
            new RegisterRequest(input.Email, input.Password, input.Name, input.TenantCode, input.DepartmentName),
            clientIp,
            cancellationToken);
        return HandleResult(result);
    }

    [Authorize]
    public async Task<AuthPayload> CreateSharedTenantAsync(
        CreateSharedTenantInput input,
        [Service] IAuthService authService,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var httpContextAccessor = context.Service<IHttpContextAccessor>();
        var user = httpContextAccessor.HttpContext?.User;

        var accountIdClaim = user?.FindFirst("sub")?.Value
                          ?? user?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        var membershipIdClaim = user?.FindFirst("MembershipId")?.Value;

        if (!Guid.TryParse(accountIdClaim, out var accountId))
            throw new GraphQLException(new HotChocolate.Error("User is not authenticated or token is invalid", "Account.Unauthorized"));

        Guid? membershipId = Guid.TryParse(membershipIdClaim, out var parsedMembershipId)
            ? parsedMembershipId
            : null;

        var result = await authService.CreateSharedTenantAsync(
            new CreateSharedTenantRequest(accountId, membershipId, input.Name, input.TenantCode, input.Currency),
            cancellationToken);

        return HandleResult(result);
    }

    [Authorize]
    public async Task<TenantApprovalPayload> CreateIsolatedTenantAsync(
        CreateIsolatedTenantInput input,
        [Service] IAuthService authService,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var httpContextAccessor = context.Service<IHttpContextAccessor>();
        var user = httpContextAccessor.HttpContext?.User;

        var accountIdClaim = user?.FindFirst("sub")?.Value
                          ?? user?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        var membershipIdClaim = user?.FindFirst("MembershipId")?.Value;

        if (!Guid.TryParse(accountIdClaim, out var accountId))
            throw new GraphQLException(new HotChocolate.Error("User is not authenticated or token is invalid", "Account.Unauthorized"));

        Guid? membershipId = Guid.TryParse(membershipIdClaim, out var parsedMembershipId)
            ? parsedMembershipId
            : null;

        var result = await authService.CreateIsolatedTenantAsync(
            new CreateIsolatedTenantRequest(
                accountId,
                membershipId,
                input.Name,
                input.TenantCode,
                input.Currency,
                new CompanyInfoRequest(
                    input.CompanyInfo.CompanyName,
                    input.CompanyInfo.TaxCode,
                    input.CompanyInfo.Address,
                    input.CompanyInfo.Phone,
                    input.CompanyInfo.ContactPerson,
                    input.CompanyInfo.BusinessType,
                    input.CompanyInfo.EmployeeCount)),
            cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new TenantApprovalPayload(
            result.Value.RequestId,
            result.Value.Status.ToString(),
            result.Value.Message,
            result.Value.ExpiresAt);
    }

    public async Task<AuthPayload> RefreshTokenAsync(
        RefreshTokenInput input,
        [Service] IAuthService authService,
        CancellationToken cancellationToken)
    {
        var result = await authService.RefreshTokenAsync(
            new RefreshTokenRequest(input.RefreshToken), cancellationToken);
        return HandleResult(result);
    }

    [Authorize]
    public async Task<AuthPayload> SwitchWorkspaceAsync(
        SwitchWorkspaceInput input,
        [Service] IAuthService authService,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var httpContextAccessor = context.Service<IHttpContextAccessor>();
        var user = httpContextAccessor.HttpContext?.User;
        var accountIdClaim = user?.FindFirst("sub")?.Value
                          ?? user?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        if (!Guid.TryParse(accountIdClaim, out var accountId))
            throw new GraphQLException(new HotChocolate.Error("User is not authenticated or token is invalid", "Account.Unauthorized"));

        var result = await authService.SwitchWorkspaceAsync(
            new SwitchWorkspaceRequest(accountId, input.MembershipId, input.CurrentRefreshToken),
            cancellationToken);

        return HandleResult(result);
    }

    [Authorize]
    public async Task<InvitationPayload> InviteMemberAsync(
        InviteMemberInput input,
        [Service] IAuthService authService,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var httpContextAccessor = context.Service<IHttpContextAccessor>();
        var user = httpContextAccessor.HttpContext?.User;

        var accountIdClaim = user?.FindFirst("sub")?.Value
                          ?? user?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        var membershipIdClaim = user?.FindFirst("MembershipId")?.Value;

        if (!Guid.TryParse(accountIdClaim, out var accountId) || !Guid.TryParse(membershipIdClaim, out var membershipId))
            throw new GraphQLException(new HotChocolate.Error("User is not authenticated or token is invalid", "Account.Unauthorized"));

        var result = await authService.InviteMemberAsync(
            new InviteMemberRequest(accountId, membershipId, input.Email, input.Role),
            cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new InvitationPayload(
            result.Value.InvitationId,
            result.Value.InviteToken,
            result.Value.Email,
            result.Value.Role,
            result.Value.IdTenant,
            result.Value.ExpiresAt);
    }

    [Authorize]
    public async Task<TenantApprovalDecisionPayload> ApproveTenantAsync(
        Guid requestId,
        [Service] IAuthService authService,
        CancellationToken cancellationToken)
    {
        var result = await authService.ApproveTenantAsync(requestId, cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new TenantApprovalDecisionPayload(
            result.Value.RequestId,
            result.Value.Status.ToString(),
            result.Value.TenantId,
            result.Value.TenantCode,
            result.Value.Name);
    }

    [Authorize]
    public async Task<TenantApprovalDecisionPayload> RejectTenantAsync(
        Guid requestId,
        string reason,
        [Service] IAuthService authService,
        CancellationToken cancellationToken)
    {
        var result = await authService.RejectTenantAsync(requestId, reason, cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new TenantApprovalDecisionPayload(
            result.Value.RequestId,
            result.Value.Status.ToString(),
            result.Value.TenantId,
            result.Value.TenantCode,
            result.Value.Name);
    }

    public async Task<AuthPayload> AcceptInviteAsync(
        AcceptInviteInput input,
        [Service] IAuthService authService,
        CancellationToken cancellationToken)
    {
        var result = await authService.AcceptInviteAsync(
            new AcceptInviteRequest(input.InviteToken, input.Password),
            cancellationToken);

        return HandleResult(result);
    }

    [Authorize]
    public async Task<bool> ChangePasswordAsync(
        ChangePasswordInput input,
        [Service] IAuthService authService,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var httpContextAccessor = context.Service<IHttpContextAccessor>();
        var user = httpContextAccessor.HttpContext?.User;
        var accountIdClaim = user?.FindFirst("sub")?.Value 
                          ?? user?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        if (!Guid.TryParse(accountIdClaim, out var accountId))
            throw new GraphQLException(new HotChocolate.Error("User is not authenticated or token is invalid", "Account.Unauthorized"));

        var result = await authService.ChangePasswordAsync(
            new ChangePasswordRequest(accountId, input.CurrentPassword, input.NewPassword), cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));
        return true;
    }

    [Authorize]
    public async Task<bool> LogoutAsync(
        string refreshToken,
        [Service] IAuthService authService,
        CancellationToken cancellationToken)
    {
        var result = await authService.LogoutAsync(refreshToken, cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));
        return true;
    }

    private static AuthPayload HandleResult(FinFlow.Domain.Abstractions.Result<AuthResponse> result)
    {
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return ToPayload(result.Value);
    }

    private static AuthPayload ToPayload(AuthResponse response) =>
        new(response.AccessToken, response.RefreshToken, response.Id, response.MembershipId, response.Email, response.Role, response.IdTenant);
}
