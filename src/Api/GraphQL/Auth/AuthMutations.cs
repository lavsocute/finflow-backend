using FinFlow.Application.Auth.Dtos;
using FinFlow.Application.Auth.Interfaces;
using FinFlow.Domain.Enums;

namespace FinFlow.Api.GraphQL.Auth;

public record LoginInput(string Email, string Password);
public record RegisterInput(string Email, string Password, string Name, string TenantCode, string DepartmentName = "Root");
public record RefreshTokenInput(string AccessToken, string RefreshToken);

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
    private readonly IAuthService _authService;

    public AuthMutations(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<AuthPayload> LoginAsync(LoginInput input, CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(new LoginRequest(input.Email, input.Password), cancellationToken);
        return ToPayload(result);
    }

    public async Task<AuthPayload> RegisterAsync(RegisterInput input, CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(
            new RegisterRequest(input.Email, input.Password, input.Name, input.TenantCode, input.DepartmentName),
            cancellationToken);
        return ToPayload(result);
    }

    public async Task<AuthPayload> RefreshTokenAsync(RefreshTokenInput input, CancellationToken cancellationToken)
    {
        var result = await _authService.RefreshTokenAsync(
            new RefreshTokenRequest(input.AccessToken, input.RefreshToken), cancellationToken);
        return ToPayload(result);
    }

    private static AuthPayload ToPayload(AuthResponse response) =>
        new(response.AccessToken, response.RefreshToken, response.Id, response.Email, response.Role, response.IdTenant, response.IdDepartment);
}
