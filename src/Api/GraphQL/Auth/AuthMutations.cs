using FinFlow.Api.Extensions;
using FinFlow.Application.Auth.Commands.ChangePassword;
using FinFlow.Application.Auth.Commands.ForgotPassword;
using FinFlow.Application.Auth.Commands.Login;
using FinFlow.Application.Auth.Commands.Logout;
using FinFlow.Application.Auth.Commands.RefreshToken;
using FinFlow.Application.Auth.Commands.Register;
using FinFlow.Application.Auth.Commands.ResetPasswordByOtp;
using FinFlow.Application.Auth.Commands.ResetPasswordByToken;
using FinFlow.Application.Auth.Commands.ResendEmailVerification;
using FinFlow.Application.Auth.Commands.SelectWorkspace;
using FinFlow.Application.Auth.Commands.VerifyEmailByOtp;
using FinFlow.Application.Auth.Commands.VerifyEmailByToken;
using FinFlow.Application.Auth.Commands.VerifyPasswordResetToken;
using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Membership.Commands.AcceptInvite;
using FinFlow.Application.Membership.Commands.InviteMember;
using FinFlow.Application.Membership.Commands.SwitchWorkspace;
using FinFlow.Application.Membership.DTOs.Requests;
using FinFlow.Application.Tenant.Commands.ApproveTenant;
using FinFlow.Application.Tenant.Commands.CreateIsolatedTenant;
using FinFlow.Application.Tenant.Commands.CreateSharedTenant;
using FinFlow.Application.Tenant.Commands.RejectTenant;
using FinFlow.Application.Tenant.DTOs.Requests;
using FinFlow.Application.Tenant.DTOs.Responses;
using FinFlow.Domain.Enums;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using MediatR;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using DomainError = FinFlow.Domain.Abstractions.Error;
using DomainResultOfAccountSession = FinFlow.Domain.Abstractions.Result<FinFlow.Application.Auth.DTOs.Responses.AccountSessionResponse>;
using DomainResultOfAuthResponse = FinFlow.Domain.Abstractions.Result<FinFlow.Application.Auth.DTOs.Responses.AuthResponse>;
using DomainResultOfRefreshSession = FinFlow.Domain.Abstractions.Result<FinFlow.Application.Auth.DTOs.Responses.RefreshSessionResponse>;
using DomainResultOfRegistrationPending = FinFlow.Domain.Abstractions.Result<FinFlow.Application.Auth.DTOs.Responses.RegistrationPendingResponse>;

namespace FinFlow.Api.GraphQL.Auth;

public record LoginInput(string Email, string Password);
public record RegisterInput(string Email, string Password, string Name);
public record CreateSharedTenantInput(string Name, string TenantCode, string Currency = "VND");
public record CompanyInfoInput(
    string? CompanyName,
    string? TaxCode,
    string? Address = null,
    string? Phone = null,
    string? ContactPerson = null,
    string? BusinessType = null,
    int? EmployeeCount = null);
public record CreateIsolatedTenantInput(string Name, string TenantCode, string Currency, CompanyInfoInput? CompanyInfo);
public record RefreshTokenInput(string RefreshToken);
public record SelectWorkspaceInput(Guid MembershipId);
public record SwitchWorkspaceInput(Guid MembershipId, string CurrentRefreshToken);
public record InviteMemberInput(string Email, RoleType Role);
public record AcceptInviteInput(string InviteToken, string Password);
public record ChangePasswordInput(string CurrentPassword, string NewPassword);
public record ResetPasswordByOtpInput(string Email, string Otp, string NewPassword);

public record AccountSessionPayload(
    string AccessToken,
    string RefreshToken,
    Guid Id,
    string Email,
    string SessionKind
);

public record RegistrationPendingPayload(
    Guid AccountId,
    string Email,
    bool RequiresEmailVerification,
    int CooldownSeconds);

public record AuthPayload(
    string AccessToken,
    string RefreshToken,
    Guid Id,
    Guid MembershipId,
    string Email,
    RoleType Role,
    Guid IdTenant,
    string SessionKind
);

public record WorkspaceSessionPayload(
    string AccessToken,
    string RefreshToken,
    Guid AccountId,
    Guid MembershipId,
    string Email,
    RoleType Role,
    Guid TenantId,
    string SessionKind
);

public record RefreshSessionPayload(
    string AccessToken,
    string RefreshToken,
    Guid Id,
    string Email,
    string SessionKind,
    Guid? MembershipId,
    RoleType? Role,
    Guid? IdTenant
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
    public async Task<AccountSessionPayload> LoginAsync(
        LoginInput input,
        [Service] IMediator mediator,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var clientIp = httpContextAccessor.HttpContext?.GetClientIpAddress();
        if (clientIp == "unknown") clientIp = null;

        var result = await mediator.Send(
            new LoginCommand(new LoginRequest(input.Email, input.Password, clientIp)),
            cancellationToken);
        return HandleAccountSessionResult(result);
    }

    public async Task<RegistrationPendingResponse> RegisterAsync(
        RegisterInput input,
        [Service] IMediator mediator,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var clientIp = httpContextAccessor.HttpContext?.GetClientIpAddress();
        if (clientIp == "unknown") clientIp = null;

        var result = await mediator.Send(
            new RegisterCommand(new RegisterRequest(input.Email, input.Password, input.Name, clientIp)),
            cancellationToken);
        return HandleRegistrationPendingResult(result);
    }

    [Authorize]
    public async Task<WorkspaceSessionPayload> SelectWorkspaceAsync(
        SelectWorkspaceInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var accountId = GetAuthenticatedAccountId(context);
        var result = await mediator.Send(
            new SelectWorkspaceCommand(new SelectWorkspaceRequest(accountId, input.MembershipId)),
            cancellationToken);

        return HandleWorkspaceResult(result);
    }

    [Authorize]
    public async Task<WorkspaceSessionPayload> CreateWorkspaceAsync(
        CreateSharedTenantInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var httpContextAccessor = context.Service<IHttpContextAccessor>();
        var user = httpContextAccessor.HttpContext?.User;
        var accountId = GetAuthenticatedAccountId(context);
        var membershipIdClaim = user?.FindFirst("MembershipId")?.Value;
        Guid? currentMembershipId = Guid.TryParse(membershipIdClaim, out var parsedMembershipId)
            ? parsedMembershipId
            : null;

        var result = await mediator.Send(
            new CreateSharedTenantCommand(new CreateSharedTenantRequest(accountId, currentMembershipId, input.Name, input.TenantCode, input.Currency)),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ToWorkspacePayload(result.Value);
    }

    [Authorize]
    public async Task<AuthPayload> CreateSharedTenantAsync(
        CreateSharedTenantInput input,
        [Service] IMediator mediator,
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

        var result = await mediator.Send(
            new CreateSharedTenantCommand(new CreateSharedTenantRequest(accountId, membershipId, input.Name, input.TenantCode, input.Currency)),
            cancellationToken);

        return HandleResult(result);
    }

    [Authorize]
    public async Task<TenantApprovalPayload> CreateIsolatedTenantAsync(
        CreateIsolatedTenantInput input,
        [Service] IMediator mediator,
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

        var companyInfoValidationError = CreateTenantInputValidation.ValidateIsolatedCompanyInfo(input.CompanyInfo);
        if (companyInfoValidationError is not null)
            throw ToGraphQlException(companyInfoValidationError);

        var companyInfo = input.CompanyInfo!;

        var result = await mediator.Send(
            new CreateIsolatedTenantCommand(
                new CreateIsolatedTenantRequest(
                    accountId,
                    membershipId,
                    input.Name,
                    input.TenantCode,
                    input.Currency,
                    new CompanyInfoRequest(
                        companyInfo.CompanyName!,
                        companyInfo.TaxCode!,
                        companyInfo.Address,
                        companyInfo.Phone,
                        companyInfo.ContactPerson,
                        companyInfo.BusinessType,
                        companyInfo.EmployeeCount))),
            cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new TenantApprovalPayload(
            result.Value.RequestId,
            result.Value.Status.ToString(),
            result.Value.Message,
            result.Value.ExpiresAt);
    }

    public async Task<RefreshSessionPayload> RefreshTokenAsync(
        RefreshTokenInput input,
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new RefreshTokenCommand(new RefreshTokenRequest(input.RefreshToken)),
            cancellationToken);
        return HandleRefreshResult(result);
    }

    public async Task<ChallengeDispatchResponse> ForgotPasswordAsync(
        string email,
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new ForgotPasswordCommand(new ForgotPasswordRequest(email)),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return result.Value;
    }

    public async Task<bool> VerifyPasswordResetTokenAsync(
        string token,
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new VerifyPasswordResetTokenCommand(token), cancellationToken);
        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return result.Value;
    }

    public async Task<bool> VerifyEmailByTokenAsync(
        string token,
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new VerifyEmailByTokenCommand(new VerifyEmailByTokenRequest(token)),
            cancellationToken);
        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return true;
    }

    public async Task<bool> VerifyEmailByOtpAsync(
        string email,
        string otp,
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new VerifyEmailByOtpCommand(new VerifyEmailByOtpRequest(email, otp)),
            cancellationToken);
        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return true;
    }

    public async Task<ChallengeDispatchResponse> ResendEmailVerificationAsync(
        string email,
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new ResendEmailVerificationCommand(new ResendEmailVerificationRequest(email)),
            cancellationToken);
        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return result.Value;
    }

    public async Task<bool> ResetPasswordByTokenAsync(
        string token,
        string newPassword,
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new ResetPasswordByTokenCommand(new ResetPasswordByTokenRequest(token, newPassword)),
            cancellationToken);
        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return true;
    }

    public async Task<bool> ResetPasswordByOtpAsync(
        ResetPasswordByOtpInput input,
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new ResetPasswordByOtpCommand(new ResetPasswordByOtpRequest(input.Email, input.Otp, input.NewPassword)),
            cancellationToken);
        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return true;
    }

    [Authorize]
    public async Task<AuthPayload> SwitchWorkspaceAsync(
        SwitchWorkspaceInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var httpContextAccessor = context.Service<IHttpContextAccessor>();
        var user = httpContextAccessor.HttpContext?.User;
        var accountIdClaim = user?.FindFirst("sub")?.Value
                          ?? user?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        if (!Guid.TryParse(accountIdClaim, out var accountId))
            throw new GraphQLException(new HotChocolate.Error("User is not authenticated or token is invalid", "Account.Unauthorized"));

        var result = await mediator.Send(
            new SwitchWorkspaceCommand(new SwitchWorkspaceRequest(accountId, input.MembershipId, input.CurrentRefreshToken)),
            cancellationToken);

        return HandleResult(result);
    }

    [Authorize]
    public async Task<InvitationPayload> InviteMemberAsync(
        InviteMemberInput input,
        [Service] IMediator mediator,
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

        var result = await mediator.Send(
            new InviteMemberCommand(new InviteMemberRequest(accountId, membershipId, input.Email, input.Role)),
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
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ApproveTenantCommand(new ApproveTenantRequest(requestId)), cancellationToken);
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
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RejectTenantCommand(new RejectTenantRequest(requestId, reason)), cancellationToken);
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
        [Service] IMediator mediator,
        [Service] IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var clientIp = httpContextAccessor.HttpContext?.GetClientIpAddress();
        if (clientIp == "unknown") clientIp = null;

        var result = await mediator.Send(
            new AcceptInviteCommand(new AcceptInviteRequest(input.InviteToken, input.Password, clientIp)),
            cancellationToken);

        return HandleResult(result);
    }

    [Authorize]
    public async Task<bool> ChangePasswordAsync(
        ChangePasswordInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var httpContextAccessor = context.Service<IHttpContextAccessor>();
        var user = httpContextAccessor.HttpContext?.User;
        var accountIdClaim = user?.FindFirst("sub")?.Value
                          ?? user?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        if (!Guid.TryParse(accountIdClaim, out var accountId))
            throw new GraphQLException(new HotChocolate.Error("User is not authenticated or token is invalid", "Account.Unauthorized"));

        var result = await mediator.Send(
            new ChangePasswordCommand(new ChangePasswordRequest(accountId, input.CurrentPassword, input.NewPassword)),
            cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));
        return true;
    }

    [Authorize]
    public async Task<bool> LogoutAsync(
        string refreshToken,
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new LogoutCommand(new LogoutRequest(refreshToken)), cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));
        return true;
    }

    private static AccountSessionPayload HandleAccountSessionResult(DomainResultOfAccountSession result)
    {
        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return new AccountSessionPayload(
            result.Value.AccessToken,
            result.Value.RefreshToken,
            result.Value.Id,
            result.Value.Email,
            result.Value.SessionKind);
    }

    private static RegistrationPendingResponse HandleRegistrationPendingResult(DomainResultOfRegistrationPending result)
    {
        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return result.Value;
    }

    private static WorkspaceSessionPayload HandleWorkspaceResult(FinFlow.Domain.Abstractions.Result<WorkspaceSessionResponse> result)
    {
        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return new WorkspaceSessionPayload(
            result.Value.AccessToken,
            result.Value.RefreshToken,
            result.Value.AccountId,
            result.Value.MembershipId,
            result.Value.Email,
            result.Value.Role,
            result.Value.TenantId,
            result.Value.SessionKind);
    }

    private static AuthPayload HandleResult(DomainResultOfAuthResponse result)
    {
        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ToPayload(result.Value);
    }

    private static RefreshSessionPayload HandleRefreshResult(DomainResultOfRefreshSession result)
    {
        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ToPayload(result.Value);
    }

    private static GraphQLException ToGraphQlException(DomainError error) =>
        new(new HotChocolate.Error(error.Description, error.Code));

    private static AuthPayload ToPayload(AuthResponse response) =>
        new(response.AccessToken, response.RefreshToken, response.Id, response.MembershipId, response.Email, response.Role, response.IdTenant, response.SessionKind);

    private static RefreshSessionPayload ToPayload(RefreshSessionResponse response) =>
        new(response.AccessToken, response.RefreshToken, response.Id, response.Email, response.SessionKind, response.MembershipId, response.Role, response.IdTenant);

    private static WorkspaceSessionPayload ToWorkspacePayload(AuthResponse response) =>
        new(response.AccessToken, response.RefreshToken, response.Id, response.MembershipId, response.Email, response.Role, response.IdTenant, response.SessionKind);

    private static Guid GetAuthenticatedAccountId(IResolverContext context)
    {
        var httpContextAccessor = context.Service<IHttpContextAccessor>();
        var user = httpContextAccessor.HttpContext?.User;
        var accountIdClaim = user?.FindFirst("sub")?.Value
                          ?? user?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        if (!Guid.TryParse(accountIdClaim, out var accountId))
            throw new GraphQLException(new HotChocolate.Error("User is not authenticated or token is invalid", "Account.Unauthorized"));

        return accountId;
    }
}
