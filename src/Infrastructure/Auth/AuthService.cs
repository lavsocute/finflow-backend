using FinFlow.Application.Auth.Dtos;
using FinFlow.Application.Auth.Interfaces;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Tenants;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Departments;
using FinFlow.Domain.RefreshTokens;

namespace FinFlow.Infrastructure.Auth;

public class AuthService : IAuthService
{
    private readonly IAccountRepository _accountRepo;
    private readonly ITenantRepository _tenantRepo;
    private readonly ITenantMembershipRepository _membershipRepo;
    private readonly IDepartmentRepository _departmentRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly JwtTokenService _tokenService;
    private readonly ILoginRateLimiter _rateLimiter;

    public AuthService(
        IAccountRepository accountRepo,
        ITenantRepository tenantRepo,
        ITenantMembershipRepository membershipRepo,
        IDepartmentRepository departmentRepo,
        IRefreshTokenRepository refreshTokenRepo,
        IUnitOfWork unitOfWork,
        JwtTokenService tokenService,
        ILoginRateLimiter rateLimiter)
    {
        _accountRepo = accountRepo;
        _tenantRepo = tenantRepo;
        _membershipRepo = membershipRepo;
        _departmentRepo = departmentRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _rateLimiter = rateLimiter;
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, string? clientIp, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepo.GetByCodeAsync(request.TenantCode.Trim(), cancellationToken);
        var tenantId = tenant?.Id;

        if (await _rateLimiter.IsBlockedAsync(clientIp, request.Email, tenantId))
            return Result.Failure<AuthResponse>(AccountErrors.TooManyRequests);

        if (tenant == null || !tenant.IsActive)
        {
            await _rateLimiter.RecordFailureAsync(clientIp, request.Email, tenantId);
            return Result.Failure<AuthResponse>(AccountErrors.InvalidCurrentPassword);
        }

        var account = await _accountRepo.GetLoginInfoForTenantAsync(request.Email, tenant.Id, cancellationToken);

        if (account == null || !BCrypt.Net.BCrypt.Verify(request.Password, account.PasswordHash))
        {
            await _rateLimiter.RecordFailureAsync(clientIp, request.Email, tenantId);
            return Result.Failure<AuthResponse>(AccountErrors.InvalidCurrentPassword);
        }

        await _rateLimiter.ResetAccountAsync(request.Email, tenant.Id);

        if (!account.IsActive)
            return Result.Failure<AuthResponse>(AccountErrors.AlreadyDeactivated);

        var accessToken = _tokenService.GenerateAccessToken(
            account.Id, account.Email, account.Role, account.IdTenant, account.IdDepartment);
        var refreshTokenStr = _tokenService.GenerateRefreshToken();

        var refreshTokenResult = RefreshToken.Create(refreshTokenStr, account.Id, _tokenService.RefreshTokenExpirationDays);
        if (refreshTokenResult.IsFailure)
            return Result.Failure<AuthResponse>(refreshTokenResult.Error);

        var refreshToken = refreshTokenResult.Value;
        _refreshTokenRepo.Add(refreshToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (!Enum.TryParse<RoleType>(account.Role, out var role))
            return Result.Failure<AuthResponse>(AccountErrors.InvalidRole);

        return Result.Success(new AuthResponse(
            accessToken, refreshTokenStr, account.Id, account.Email, role, account.IdTenant, account.IdDepartment));
    }

    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var existingAccount = await _accountRepo.ExistsByEmailIgnoringTenantAsync(request.Email, cancellationToken);
        if (existingAccount)
            return Result.Failure<AuthResponse>(AccountErrors.EmailAlreadyExists);

        var existingTenant = await _tenantRepo.ExistsByCodeAsync(request.TenantCode, cancellationToken);
        if (existingTenant)
            return Result.Failure<AuthResponse>(TenantErrors.CodeAlreadyExists);

        var tenantResult = Tenant.Create(request.Name, request.TenantCode, TenancyModel.Shared, "VND");
        if (tenantResult.IsFailure)
            return Result.Failure<AuthResponse>(tenantResult.Error);

        var tenant = tenantResult.Value;

        var departmentResult = Department.Create(request.DepartmentName, tenant.Id);
        if (departmentResult.IsFailure)
            return Result.Failure<AuthResponse>(departmentResult.Error);

        var department = departmentResult.Value;

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        var accountResult = Account.Create(request.Email, passwordHash, RoleType.TenantAdmin, tenant.Id, department.Id);
        if (accountResult.IsFailure)
            return Result.Failure<AuthResponse>(accountResult.Error);

        var account = accountResult.Value;

        var membershipResult = TenantMembership.Create(account.Id, tenant.Id, RoleType.TenantAdmin);
        if (membershipResult.IsFailure)
            return Result.Failure<AuthResponse>(membershipResult.Error);

        var membership = membershipResult.Value;

        var refreshTokenStr = _tokenService.GenerateRefreshToken();
        var refreshTokenResult = RefreshToken.Create(refreshTokenStr, account.Id, _tokenService.RefreshTokenExpirationDays);
        if (refreshTokenResult.IsFailure)
            return Result.Failure<AuthResponse>(refreshTokenResult.Error);

        var refreshToken = refreshTokenResult.Value;

        _tenantRepo.Add(tenant);
        _departmentRepo.Add(department);
        _accountRepo.Add(account);
        _membershipRepo.Add(membership);
        _refreshTokenRepo.Add(refreshToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(
            account.Id, account.Email, account.Role.ToString(), account.IdTenant, account.IdDepartment);

        return Result.Success(new AuthResponse(
            accessToken, refreshTokenStr, account.Id, account.Email, account.Role, account.IdTenant, account.IdDepartment));
    }

    public async Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var storedToken = await _refreshTokenRepo.GetByTokenAsync(request.RefreshToken, cancellationToken);

        if (storedToken == null)
            return Result.Failure<AuthResponse>(RefreshTokenErrors.NotFound);

        if (!storedToken.IsActive)
            return storedToken.IsRevoked
                ? Result.Failure<AuthResponse>(RefreshTokenErrors.Revoked)
                : Result.Failure<AuthResponse>(RefreshTokenErrors.Expired);

        var account = await _accountRepo.GetLoginInfoByIdAsync(storedToken.AccountId, cancellationToken);
        if (account == null || !account.IsActive)
            return Result.Failure<AuthResponse>(AccountErrors.AlreadyDeactivated);

        var newRawToken = _tokenService.GenerateRefreshToken();
        var replaceResult = storedToken.ReplaceWith(newRawToken, _tokenService.RefreshTokenExpirationDays);
        if (replaceResult.IsFailure)
            return Result.Failure<AuthResponse>(replaceResult.Error);

        var (newRefreshTokenEntity, rawTokenForClient) = replaceResult.Value;
        
        _refreshTokenRepo.Add(newRefreshTokenEntity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var newAccessToken = _tokenService.GenerateAccessToken(account.Id, account.Email, account.Role, account.IdTenant, account.IdDepartment);

        if (!Enum.TryParse<RoleType>(account.Role, out var role))
            return Result.Failure<AuthResponse>(AccountErrors.InvalidRole);

        return Result.Success(new AuthResponse(
            newAccessToken, rawTokenForClient, account.Id, account.Email, role, account.IdTenant, account.IdDepartment));
    }

    public async Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var accountInfo = await _accountRepo.GetLoginInfoByIdAsync(request.AccountId, cancellationToken);

        if (accountInfo == null)
            return Result.Failure(AccountErrors.NotFound);

        if (!accountInfo.IsActive)
            return Result.Failure(AccountErrors.AlreadyDeactivated);

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, accountInfo.PasswordHash))
            return Result.Failure(AccountErrors.InvalidCurrentPassword);

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            return Result.Failure(AccountErrors.PasswordTooShort);

        var newPasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

        var account = await _accountRepo.GetByIdForUpdateAsync(accountInfo.Id, cancellationToken);
        if (account == null)
            return Result.Failure(AccountErrors.NotFound);

        var changeResult = account.ChangePassword(newPasswordHash);
        if (changeResult.IsFailure)
            return changeResult;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var revoked = await _refreshTokenRepo.RevokeByTokenAsync(refreshToken, "User logout", cancellationToken);
        if (!revoked)
            return Result.Failure(RefreshTokenErrors.NotFound);
        return Result.Success();
    }
}
