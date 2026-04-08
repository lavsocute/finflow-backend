using FinFlow.Application.Auth.Dtos;
using FinFlow.Application.Auth.Interfaces;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.Tenants;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Invitations;
using FinFlow.Domain.RefreshTokens;
using System.Text.RegularExpressions;

namespace FinFlow.Infrastructure.Auth;

public class AuthService : IAuthService
{
    private readonly IAccountRepository _accountRepo;
    private readonly ITenantRepository _tenantRepo;
    private readonly ITenantMembershipRepository _membershipRepo;
    private readonly IDepartmentRepository _departmentRepo;
    private readonly IInvitationRepository _invitationRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly JwtTokenService _tokenService;
    private readonly ILoginRateLimiter _rateLimiter;
    private readonly ICurrentTenant _currentTenant;
    private static readonly Regex _strongPasswordRegex = new(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).{8,}$",
        RegexOptions.Compiled);

    public AuthService(
        IAccountRepository accountRepo,
        ITenantRepository tenantRepo,
        ITenantMembershipRepository membershipRepo,
        IDepartmentRepository departmentRepo,
        IInvitationRepository invitationRepo,
        IRefreshTokenRepository refreshTokenRepo,
        IUnitOfWork unitOfWork,
        JwtTokenService tokenService,
        ILoginRateLimiter rateLimiter,
        ICurrentTenant currentTenant)
    {
        _accountRepo = accountRepo;
        _tenantRepo = tenantRepo;
        _membershipRepo = membershipRepo;
        _departmentRepo = departmentRepo;
        _invitationRepo = invitationRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _rateLimiter = rateLimiter;
        _currentTenant = currentTenant;
    }

    private AuthResponse CreateAuthResponse(AccountLoginInfo account, TenantMembershipSummary membership, string accessToken, string refreshToken) =>
        new(accessToken, refreshToken, account.Id, membership.Id, account.Email, membership.Role, membership.IdTenant);

    private static bool IsStrongPassword(string password) =>
        !string.IsNullOrWhiteSpace(password) && _strongPasswordRegex.IsMatch(password);

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

        var account = await _accountRepo.GetLoginInfoByEmailAsync(request.Email, cancellationToken);

        if (account == null || !BCrypt.Net.BCrypt.Verify(request.Password, account.PasswordHash))
        {
            await _rateLimiter.RecordFailureAsync(clientIp, request.Email, tenantId);
            return Result.Failure<AuthResponse>(AccountErrors.InvalidCurrentPassword);
        }

        await _rateLimiter.ResetAccountAsync(request.Email, tenant.Id);

        if (!account.IsActive)
            return Result.Failure<AuthResponse>(AccountErrors.AlreadyDeactivated);

        var membership = await _membershipRepo.GetActiveByAccountAndTenantAsync(account.Id, tenant.Id, cancellationToken);
        if (membership == null)
        {
            await _rateLimiter.RecordFailureAsync(clientIp, request.Email, tenantId);
            return Result.Failure<AuthResponse>(AccountErrors.InvalidCurrentPassword);
        }

        var accessToken = _tokenService.GenerateAccessToken(
            account.Id, account.Email, membership.Role.ToString(), membership.IdTenant, membership.Id);
        var refreshTokenStr = _tokenService.GenerateRefreshToken();

        var refreshTokenResult = RefreshToken.Create(refreshTokenStr, account.Id, membership.Id, _tokenService.RefreshTokenExpirationDays);
        if (refreshTokenResult.IsFailure)
            return Result.Failure<AuthResponse>(refreshTokenResult.Error);

        var refreshToken = refreshTokenResult.Value;
        _refreshTokenRepo.Add(refreshToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(CreateAuthResponse(account, membership, accessToken, refreshTokenStr));
    }

    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, string? clientIp, CancellationToken cancellationToken = default)
    {
        if (await _rateLimiter.IsBlockedAsync(clientIp, request.Email))
            return Result.Failure<AuthResponse>(AccountErrors.TooManyRequests);

        var existingAccount = await _accountRepo.ExistsByEmailIgnoringTenantAsync(request.Email, cancellationToken);
        if (existingAccount)
        {
            await _rateLimiter.RecordFailureAsync(clientIp, request.Email);
            return Result.Failure<AuthResponse>(AccountErrors.EmailAlreadyExists);
        }

        var existingTenant = await _tenantRepo.ExistsByCodeAsync(request.TenantCode, cancellationToken);
        if (existingTenant)
        {
            await _rateLimiter.RecordFailureAsync(clientIp, request.Email);
            return Result.Failure<AuthResponse>(TenantErrors.CodeAlreadyExists);
        }

        if (!IsStrongPassword(request.Password))
        {
            await _rateLimiter.RecordFailureAsync(clientIp, request.Email);
            return Result.Failure<AuthResponse>(AccountErrors.PasswordTooWeak);
        }

        var tenantResult = Tenant.Create(request.Name, request.TenantCode, TenancyModel.Shared, "VND");
        if (tenantResult.IsFailure)
        {
            await _rateLimiter.RecordFailureAsync(clientIp, request.Email);
            return Result.Failure<AuthResponse>(tenantResult.Error);
        }

        var tenant = tenantResult.Value;

        var departmentResult = Department.Create(request.DepartmentName, tenant.Id);
        if (departmentResult.IsFailure)
        {
            await _rateLimiter.RecordFailureAsync(clientIp, request.Email);
            return Result.Failure<AuthResponse>(departmentResult.Error);
        }

        var department = departmentResult.Value;

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        var accountResult = Account.Create(request.Email, passwordHash, department.Id);
        if (accountResult.IsFailure)
        {
            await _rateLimiter.RecordFailureAsync(clientIp, request.Email);
            return Result.Failure<AuthResponse>(accountResult.Error);
        }

        var account = accountResult.Value;

        var membershipResult = TenantMembership.Create(account.Id, tenant.Id, RoleType.TenantAdmin);
        if (membershipResult.IsFailure)
        {
            await _rateLimiter.RecordFailureAsync(clientIp, request.Email);
            return Result.Failure<AuthResponse>(membershipResult.Error);
        }

        var membership = membershipResult.Value;

        var refreshTokenStr = _tokenService.GenerateRefreshToken();
        var refreshTokenResult = RefreshToken.Create(refreshTokenStr, account.Id, membership.Id, _tokenService.RefreshTokenExpirationDays);
        if (refreshTokenResult.IsFailure)
        {
            await _rateLimiter.RecordFailureAsync(clientIp, request.Email);
            return Result.Failure<AuthResponse>(refreshTokenResult.Error);
        }

        var refreshToken = refreshTokenResult.Value;

        _tenantRepo.Add(tenant);
        _departmentRepo.Add(department);
        _accountRepo.Add(account);
        _membershipRepo.Add(membership);
        _refreshTokenRepo.Add(refreshToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _rateLimiter.ResetAccountAsync(request.Email);

        var accessToken = _tokenService.GenerateAccessToken(
            account.Id, account.Email, membership.Role.ToString(), membership.IdTenant, membership.Id);

        return Result.Success(CreateAuthResponse(
            new AccountLoginInfo(account.Id, account.Email, account.PasswordHash, account.IsActive),
            new TenantMembershipSummary(membership.Id, membership.AccountId, membership.IdTenant, membership.Role, membership.IsActive, membership.CreatedAt),
            accessToken,
            refreshTokenStr));
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

        var membership = await _membershipRepo.GetByIdAsync(storedToken.MembershipId, cancellationToken);
        if (membership == null || !membership.IsActive)
            return Result.Failure<AuthResponse>(TenantMembershipErrors.NotFound);

        var newRawToken = _tokenService.GenerateRefreshToken();
        var replaceResult = storedToken.ReplaceWith(newRawToken, _tokenService.RefreshTokenExpirationDays);
        if (replaceResult.IsFailure)
            return Result.Failure<AuthResponse>(replaceResult.Error);

        var (newRefreshTokenEntity, rawTokenForClient) = replaceResult.Value;
        
        _refreshTokenRepo.Add(newRefreshTokenEntity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var newAccessToken = _tokenService.GenerateAccessToken(
            account.Id, account.Email, membership.Role.ToString(), membership.IdTenant, membership.Id);

        return Result.Success(CreateAuthResponse(account, membership, newAccessToken, rawTokenForClient));
    }

    public async Task<Result<AuthResponse>> SwitchWorkspaceAsync(SwitchWorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentRefreshToken))
            return Result.Failure<AuthResponse>(RefreshTokenErrors.NotFound);

        var account = await _accountRepo.GetLoginInfoByIdAsync(request.AccountId, cancellationToken);
        if (account == null || !account.IsActive)
            return Result.Failure<AuthResponse>(AccountErrors.AlreadyDeactivated);

        var membership = await _membershipRepo.GetByIdAsync(request.MembershipId, cancellationToken);
        if (membership == null || !membership.IsActive)
            return Result.Failure<AuthResponse>(TenantMembershipErrors.NotFound);

        if (membership.AccountId != request.AccountId)
            return Result.Failure<AuthResponse>(AccountErrors.Unauthorized);

        var currentRefreshToken = await _refreshTokenRepo.GetByTokenAsync(request.CurrentRefreshToken, cancellationToken);
        if (currentRefreshToken == null)
            return Result.Failure<AuthResponse>(RefreshTokenErrors.NotFound);

        if (!currentRefreshToken.IsActive)
            return currentRefreshToken.IsRevoked
                ? Result.Failure<AuthResponse>(RefreshTokenErrors.Revoked)
                : Result.Failure<AuthResponse>(RefreshTokenErrors.Expired);

        if (currentRefreshToken.AccountId != request.AccountId)
            return Result.Failure<AuthResponse>(AccountErrors.Unauthorized);

        var revokeResult = currentRefreshToken.Revoke("Workspace switched");
        if (revokeResult.IsFailure)
            return Result.Failure<AuthResponse>(revokeResult.Error);

        var newRefreshTokenRaw = _tokenService.GenerateRefreshToken();
        var newRefreshTokenResult = RefreshToken.Create(
            newRefreshTokenRaw,
            account.Id,
            membership.Id,
            _tokenService.RefreshTokenExpirationDays);

        if (newRefreshTokenResult.IsFailure)
            return Result.Failure<AuthResponse>(newRefreshTokenResult.Error);

        _refreshTokenRepo.Add(newRefreshTokenResult.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(
            account.Id,
            account.Email,
            membership.Role.ToString(),
            membership.IdTenant,
            membership.Id);

        return Result.Success(CreateAuthResponse(account, membership, accessToken, newRefreshTokenRaw));
    }

    public async Task<Result<InvitationResponse>> InviteMemberAsync(InviteMemberRequest request, CancellationToken cancellationToken = default)
    {
        var inviterAccount = await _accountRepo.GetLoginInfoByIdAsync(request.InviterAccountId, cancellationToken);
        if (inviterAccount == null || !inviterAccount.IsActive)
            return Result.Failure<InvitationResponse>(AccountErrors.Unauthorized);

        var inviterMembership = await _membershipRepo.GetByIdAsync(request.InviterMembershipId, cancellationToken);
        if (inviterMembership == null || !inviterMembership.IsActive)
            return Result.Failure<InvitationResponse>(TenantMembershipErrors.NotFound);

        if (inviterMembership.AccountId != request.InviterAccountId)
            return Result.Failure<InvitationResponse>(AccountErrors.Unauthorized);

        if (inviterMembership.Role != RoleType.TenantAdmin)
            return Result.Failure<InvitationResponse>(InvitationErrors.Forbidden);

        if (request.Role is RoleType.SuperAdmin)
            return Result.Failure<InvitationResponse>(InvitationErrors.InvalidRole);

        var tenant = await _tenantRepo.GetByIdAsync(inviterMembership.IdTenant, cancellationToken);
        if (tenant == null || !tenant.IsActive)
            return Result.Failure<InvitationResponse>(TenantErrors.NotFound);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var existingAccount = await _accountRepo.GetLoginInfoByEmailAsync(normalizedEmail, cancellationToken);
        if (existingAccount != null)
        {
            var alreadyMember = await _membershipRepo.ExistsAsync(existingAccount.Id, inviterMembership.IdTenant, cancellationToken);
            if (alreadyMember)
                return Result.Failure<InvitationResponse>(InvitationErrors.AlreadyMember);
        }

        var pendingInvitationExists = await _invitationRepo.HasPendingInvitationAsync(normalizedEmail, inviterMembership.IdTenant, cancellationToken);
        if (pendingInvitationExists)
            return Result.Failure<InvitationResponse>(InvitationErrors.PendingInvitationExists);

        var rawInviteToken = _tokenService.GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddDays(7);
        var invitationResult = Invitation.Create(
            normalizedEmail,
            inviterMembership.IdTenant,
            inviterMembership.Id,
            request.Role,
            rawInviteToken,
            expiresAt);

        if (invitationResult.IsFailure)
            return Result.Failure<InvitationResponse>(invitationResult.Error);

        var invitation = invitationResult.Value;
        _invitationRepo.Add(invitation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new InvitationResponse(
            invitation.Id,
            rawInviteToken,
            invitation.Email,
            invitation.Role,
            invitation.IdTenant,
            invitation.ExpiresAt));
    }

    public async Task<Result<AuthResponse>> AcceptInviteAsync(AcceptInviteRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.InviteToken))
            return Result.Failure<AuthResponse>(InvitationErrors.TokenRequired);

        if (string.IsNullOrWhiteSpace(request.Password))
            return Result.Failure<AuthResponse>(InvitationErrors.PasswordRequired);

        var invitation = await _invitationRepo.GetByTokenForUpdateAsync(request.InviteToken, cancellationToken);
        if (invitation == null)
            return Result.Failure<AuthResponse>(InvitationErrors.NotFound);

        var tenant = await _tenantRepo.GetByIdAsync(invitation.IdTenant, cancellationToken);
        if (tenant == null || !tenant.IsActive)
            return Result.Failure<AuthResponse>(TenantErrors.NotFound);

        var existingAccount = await _accountRepo.GetLoginInfoByEmailAsync(invitation.Email, cancellationToken);
        AccountLoginInfo accountInfo;
        Guid accountId;

        if (existingAccount != null)
        {
            if (!existingAccount.IsActive)
                return Result.Failure<AuthResponse>(AccountErrors.AlreadyDeactivated);

            if (!BCrypt.Net.BCrypt.Verify(request.Password, existingAccount.PasswordHash))
                return Result.Failure<AuthResponse>(AccountErrors.InvalidCurrentPassword);

            var alreadyMember = await _membershipRepo.ExistsAsync(existingAccount.Id, invitation.IdTenant, cancellationToken);
            if (alreadyMember)
                return Result.Failure<AuthResponse>(InvitationErrors.AlreadyMember);

            accountInfo = existingAccount;
            accountId = existingAccount.Id;
        }
        else
        {
            if (!IsStrongPassword(request.Password))
                return Result.Failure<AuthResponse>(AccountErrors.PasswordTooWeak);

            var defaultDepartment = await _departmentRepo.GetDefaultByTenantIdAsync(invitation.IdTenant, cancellationToken);
            if (defaultDepartment == null)
                return Result.Failure<AuthResponse>(DepartmentErrors.NotFound);

            var createAccountResult = Account.Create(
                invitation.Email,
                BCrypt.Net.BCrypt.HashPassword(request.Password),
                defaultDepartment.Id);

            if (createAccountResult.IsFailure)
                return Result.Failure<AuthResponse>(createAccountResult.Error);

            var account = createAccountResult.Value;
            _accountRepo.Add(account);
            accountInfo = new AccountLoginInfo(account.Id, account.Email, account.PasswordHash, account.IsActive);
            accountId = account.Id;
        }

        var membershipResult = TenantMembership.Create(accountId, invitation.IdTenant, invitation.Role);
        if (membershipResult.IsFailure)
            return Result.Failure<AuthResponse>(membershipResult.Error);

        var membership = membershipResult.Value;

        var acceptResult = invitation.MarkAccepted();
        if (acceptResult.IsFailure)
            return Result.Failure<AuthResponse>(acceptResult.Error);

        var refreshTokenRaw = _tokenService.GenerateRefreshToken();
        var refreshTokenResult = RefreshToken.Create(
            refreshTokenRaw,
            accountId,
            membership.Id,
            _tokenService.RefreshTokenExpirationDays);

        if (refreshTokenResult.IsFailure)
            return Result.Failure<AuthResponse>(refreshTokenResult.Error);

        var originalTenantId = _currentTenant.Id;
        var originalMembershipId = _currentTenant.MembershipId;
        var originalIsSuperAdmin = _currentTenant.IsSuperAdmin;

        try
        {
            _currentTenant.Id = invitation.IdTenant;
            _currentTenant.MembershipId = membership.Id;
            _currentTenant.IsSuperAdmin = false;

            _membershipRepo.Add(membership);
            _invitationRepo.Update(invitation);
            _refreshTokenRepo.Add(refreshTokenResult.Value);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _currentTenant.Id = originalTenantId;
            _currentTenant.MembershipId = originalMembershipId;
            _currentTenant.IsSuperAdmin = originalIsSuperAdmin;
        }

        var accessToken = _tokenService.GenerateAccessToken(
            accountInfo.Id,
            accountInfo.Email,
            membership.Role.ToString(),
            membership.IdTenant,
            membership.Id);

        return Result.Success(CreateAuthResponse(
            accountInfo,
            new TenantMembershipSummary(membership.Id, membership.AccountId, membership.IdTenant, membership.Role, membership.IsActive, membership.CreatedAt),
            accessToken,
            refreshTokenRaw));
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

        if (!IsStrongPassword(request.NewPassword))
            return Result.Failure(AccountErrors.PasswordTooWeak);

        var newPasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

        var account = await _accountRepo.GetByIdForUpdateAsync(accountInfo.Id, cancellationToken);
        if (account == null)
            return Result.Failure(AccountErrors.NotFound);

        var changeResult = account.ChangePassword(newPasswordHash);
        if (changeResult.IsFailure)
            return changeResult;

        await _refreshTokenRepo.RevokeAllForAccountAsync(account.Id, "Password changed", cancellationToken);
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
