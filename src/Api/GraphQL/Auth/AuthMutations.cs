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

public record LoginInput(string Email, string Password);
public record RegisterInput(string Email, string Password, string Name, string TenantCode, string DepartmentName = "Root");
public record RefreshTokenInput(string RefreshToken);
public record ChangePasswordInput(string CurrentPassword, string NewPassword);

public record AuthPayload(
    string AccessToken,
    string RefreshToken,
    Guid Id,
    string Email,
    RoleType Role,
    Guid IdTenant,
    Guid IdDepartment
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
        
        var result = await authService.LoginAsync(new LoginRequest(input.Email, input.Password), clientIp, cancellationToken);
        return HandleResult(result);
    }

    public async Task<AuthPayload> RegisterAsync(
        RegisterInput input,
        [Service] IAuthService authService,
        CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(
            new RegisterRequest(input.Email, input.Password, input.Name, input.TenantCode, input.DepartmentName),
            cancellationToken);
        return HandleResult(result);
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
        new(response.AccessToken, response.RefreshToken, response.Id, response.Email, response.Role, response.IdTenant, response.IdDepartment);
}
