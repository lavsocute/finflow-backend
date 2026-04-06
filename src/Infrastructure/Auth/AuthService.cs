using FinFlow.Application.Auth.Dtos;
using FinFlow.Application.Auth.Interfaces;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Auth;

public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _context;
    private readonly JwtTokenService _tokenService;

    public AuthService(ApplicationDbContext context, JwtTokenService tokenService)
    {
        _context = context;
        _tokenService = tokenService;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var account = await _context.Accounts
            .Include(a => a.Tenant)
            .Include(a => a.Department)
            .FirstOrDefaultAsync(a => a.Email == request.Email && !a.IsDeleted, cancellationToken);

        if (account == null || !BCrypt.Net.BCrypt.Verify(request.Password, account.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password");

        var accessToken = _tokenService.GenerateAccessToken(
            account.Id, account.Email, account.Role.ToString(), account.IdTenant, account.IdDepartment);
        var refreshToken = _tokenService.GenerateRefreshToken();

        return new AuthResponse(
            accessToken, refreshToken, account.Id, account.Email, account.Role, account.IdTenant, account.IdDepartment);
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var existingAccount = await _context.Accounts
            .AnyAsync(a => a.Email == request.Email && !a.IsDeleted, cancellationToken);

        if (existingAccount)
            throw new InvalidOperationException("Email already exists");

        var existingTenant = await _context.Tenants
            .AnyAsync(t => t.TenantCode == request.TenantCode && !t.IsDeleted, cancellationToken);

        if (existingTenant)
            throw new InvalidOperationException("Tenant code already exists");

        var tenant = new Tenant
        {
            Name = request.Name,
            TenantCode = request.TenantCode,
            TenancyModel = TenancyModel.Shared,
            Currency = "VND"
        };

        await _context.AddEntityAsync(tenant, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        var department = new Department
        {
            Name = request.DepartmentName,
            IdTenant = tenant.Id
        };

        await _context.AddEntityAsync(department, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        var account = new Account
        {
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = RoleType.TenantAdmin,
            IdTenant = tenant.Id,
            IdDepartment = department.Id
        };

        await _context.AddEntityAsync(account, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(
            account.Id, account.Email, account.Role.ToString(), account.IdTenant, account.IdDepartment);
        var refreshToken = _tokenService.GenerateRefreshToken();

        return new AuthResponse(
            accessToken, refreshToken, account.Id, account.Email, account.Role, account.IdTenant, account.IdDepartment);
    }

    public Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var principal = _tokenService.ValidateToken(request.AccessToken);
        if (principal == null)
            throw new UnauthorizedAccessException("Invalid token");

        var id = Guid.Parse(principal.FindFirst("sub")?.Value ?? Guid.Empty.ToString());
        var email = principal.FindFirst("email")?.Value ?? string.Empty;
        var role = principal.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value ?? string.Empty;
        var idTenant = Guid.Parse(principal.FindFirst("IdTenant")?.Value ?? Guid.Empty.ToString());
        var idDepartment = Guid.Parse(principal.FindFirst("IdDepartment")?.Value ?? Guid.Empty.ToString());

        var newAccessToken = _tokenService.GenerateAccessToken(id, email, role, idTenant, idDepartment);
        var newRefreshToken = _tokenService.GenerateRefreshToken();

        return Task.FromResult(new AuthResponse(
            newAccessToken, newRefreshToken, id, email, Enum.Parse<RoleType>(role), idTenant, idDepartment));
    }
}
