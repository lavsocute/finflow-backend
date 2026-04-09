using FinFlow.Application.Auth.Dtos;
using FinFlow.Application.Auth.Interfaces;
using FinFlow.Application.Auth.Commands.ChangePassword;
using FinFlow.Application.Auth.Commands.Login;
using FinFlow.Application.Auth.Commands.Logout;
using FinFlow.Application.Auth.Commands.RefreshToken;
using FinFlow.Application.Auth.Commands.Register;
using FinFlow.Application.Auth.Responses;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Membership.Responses;
using FinFlow.Application.Tenant.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.Tenants;
using FinFlow.Domain.TenantApprovals;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Invitations;
using FinFlow.Domain.RefreshTokens;
using MediatR;
using System.Text.RegularExpressions;

namespace FinFlow.Infrastructure.Auth;

public class AuthService : IAuthService
{
    private readonly IAccountRepository _accountRepo;
    private readonly ITenantRepository _tenantRepo;
    private readonly ITenantApprovalRequestRepository _tenantApprovalRequestRepo;
    private readonly ITenantMembershipRepository _membershipRepo;
    private readonly IDepartmentRepository _departmentRepo;
    private readonly IInvitationRepository _invitationRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly JwtTokenService _tokenService;
    private readonly ILoginRateLimiter _rateLimiter;
    private readonly ICurrentTenant _currentTenant;
    private readonly IMediator _mediator;
    private static readonly Regex _strongPasswordRegex = new(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).{8,}$",
        RegexOptions.Compiled);

    public AuthService(
        IAccountRepository accountRepo,
        ITenantRepository tenantRepo,
        ITenantApprovalRequestRepository tenantApprovalRequestRepo,
        ITenantMembershipRepository membershipRepo,
        IDepartmentRepository departmentRepo,
        IInvitationRepository invitationRepo,
        IRefreshTokenRepository refreshTokenRepo,
        IUnitOfWork unitOfWork,
        JwtTokenService tokenService,
        ILoginRateLimiter rateLimiter,
        ICurrentTenant currentTenant,
        IMediator mediator)
    {
        _accountRepo = accountRepo;
        _tenantRepo = tenantRepo;
        _tenantApprovalRequestRepo = tenantApprovalRequestRepo;
        _membershipRepo = membershipRepo;
        _departmentRepo = departmentRepo;
        _invitationRepo = invitationRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _rateLimiter = rateLimiter;
        _currentTenant = currentTenant;
        _mediator = mediator;
    }

    private AuthResponse CreateAuthResponse(AccountLoginInfo account, TenantMembershipSummary membership, string accessToken, string refreshToken) =>
        new(accessToken, refreshToken, account.Id, membership.Id, account.Email, membership.Role, membership.IdTenant);

    private static bool IsStrongPassword(string password) =>
        !string.IsNullOrWhiteSpace(password) && _strongPasswordRegex.IsMatch(password);

    private async Task<Result> EnsureTenantCreationActorAsync(Guid accountId, Guid? currentMembershipId, CancellationToken cancellationToken)
    {
        var account = await _accountRepo.GetLoginInfoByIdAsync(accountId, cancellationToken);
        if (account == null || !account.IsActive)
            return Result.Failure(AccountErrors.Unauthorized);

        if (_currentTenant.IsSuperAdmin)
            return Result.Success();

        if (!currentMembershipId.HasValue)
            return Result.Failure(AccountErrors.Unauthorized);

        var currentMembership = await _membershipRepo.GetByIdAsync(currentMembershipId.Value, cancellationToken);
        if (currentMembership == null || !currentMembership.IsActive)
            return Result.Failure(AccountErrors.Unauthorized);

        if (currentMembership.AccountId != accountId)
            return Result.Failure(AccountErrors.Unauthorized);

        if (currentMembership.Role != RoleType.TenantAdmin)
            return Result.Failure(TenantErrors.Forbidden);

        return Result.Success();
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, string? clientIp, CancellationToken cancellationToken = default)
    {
        return await _mediator.Send(
            new LoginCommand(request.Email, request.Password, request.TenantCode, clientIp),
            cancellationToken);
    }

    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, string? clientIp, CancellationToken cancellationToken = default)
    {
        return await _mediator.Send(
            new RegisterCommand(request.Email, request.Password, request.Name, request.TenantCode, request.DepartmentName, clientIp),
            cancellationToken);
    }

    public async Task<Result<AuthResponse>> CreateSharedTenantAsync(CreateSharedTenantRequest request, CancellationToken cancellationToken = default)
    {
        var actorCheck = await EnsureTenantCreationActorAsync(request.AccountId, request.CurrentMembershipId, cancellationToken);
        if (actorCheck.IsFailure)
            return Result.Failure<AuthResponse>(actorCheck.Error);

        var account = await _accountRepo.GetLoginInfoByIdAsync(request.AccountId, cancellationToken);
        if (account == null || !account.IsActive)
            return Result.Failure<AuthResponse>(AccountErrors.Unauthorized);

        if (await _membershipRepo.ExistsOwnerByAccountIdAsync(request.AccountId, cancellationToken))
            return Result.Failure<AuthResponse>(TenantErrors.UserAlreadyHasTenant);

        if (await _tenantRepo.ExistsByCodeAsync(request.TenantCode, cancellationToken))
            return Result.Failure<AuthResponse>(TenantErrors.CodeAlreadyExists);

        if (await _tenantApprovalRequestRepo.IsTenantCodeBlockedAsync(request.TenantCode, DateTime.UtcNow, cancellationToken))
            return Result.Failure<AuthResponse>(TenantErrors.CodeBlocked);

        var tenantResult = Tenant.Create(request.Name, request.TenantCode, TenancyModel.Shared, request.Currency);
        if (tenantResult.IsFailure)
            return Result.Failure<AuthResponse>(tenantResult.Error);

        var tenant = tenantResult.Value;

        var departmentResult = Department.Create("Root", tenant.Id);
        if (departmentResult.IsFailure)
            return Result.Failure<AuthResponse>(departmentResult.Error);

        var department = departmentResult.Value;

        var membershipResult = TenantMembership.Create(account.Id, tenant.Id, RoleType.TenantAdmin, isOwner: true);
        if (membershipResult.IsFailure)
            return Result.Failure<AuthResponse>(membershipResult.Error);

        var membership = membershipResult.Value;

        var refreshTokenRaw = _tokenService.GenerateRefreshToken();
        var refreshTokenResult = RefreshToken.Create(
            refreshTokenRaw,
            account.Id,
            membership.Id,
            _tokenService.RefreshTokenExpirationDays);

        if (refreshTokenResult.IsFailure)
            return Result.Failure<AuthResponse>(refreshTokenResult.Error);

        var originalTenantId = _currentTenant.Id;
        var originalMembershipId = _currentTenant.MembershipId;
        var originalIsSuperAdmin = _currentTenant.IsSuperAdmin;

        try
        {
            _currentTenant.Id = tenant.Id;
            _currentTenant.MembershipId = membership.Id;
            _currentTenant.IsSuperAdmin = false;

            _tenantRepo.Add(tenant);
            _departmentRepo.Add(department);
            _membershipRepo.Add(membership);
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
            account.Id,
            account.Email,
            membership.Role.ToString(),
            membership.IdTenant,
            membership.Id);

        return Result.Success(CreateAuthResponse(
            account,
            new TenantMembershipSummary(membership.Id, membership.AccountId, membership.IdTenant, membership.Role, membership.IsOwner, membership.IsActive, membership.CreatedAt),
            accessToken,
            refreshTokenRaw));
    }

    public async Task<Result<TenantApprovalResponse>> CreateIsolatedTenantAsync(CreateIsolatedTenantRequest request, CancellationToken cancellationToken = default)
    {
        var actorCheck = await EnsureTenantCreationActorAsync(request.AccountId, request.CurrentMembershipId, cancellationToken);
        if (actorCheck.IsFailure)
            return Result.Failure<TenantApprovalResponse>(actorCheck.Error);

        if (await _membershipRepo.ExistsOwnerByAccountIdAsync(request.AccountId, cancellationToken))
            return Result.Failure<TenantApprovalResponse>(TenantErrors.UserAlreadyHasTenant);

        if (await _tenantRepo.ExistsByCodeAsync(request.TenantCode, cancellationToken)
            || await _tenantApprovalRequestRepo.ExistsPendingByTenantCodeAsync(request.TenantCode, cancellationToken))
            return Result.Failure<TenantApprovalResponse>(TenantErrors.CodeAlreadyExists);

        if (await _tenantApprovalRequestRepo.IsTenantCodeBlockedAsync(request.TenantCode, DateTime.UtcNow, cancellationToken))
            return Result.Failure<TenantApprovalResponse>(TenantErrors.CodeBlocked);

        var approvalResult = TenantApprovalRequest.Create(
            request.TenantCode,
            request.Name,
            request.CompanyInfo.CompanyName,
            request.CompanyInfo.TaxCode,
            request.CompanyInfo.Address,
            request.CompanyInfo.Phone,
            request.CompanyInfo.ContactPerson,
            request.CompanyInfo.BusinessType,
            request.CompanyInfo.EmployeeCount,
            request.Currency,
            request.AccountId,
            DateTime.UtcNow.AddDays(7));

        if (approvalResult.IsFailure)
            return Result.Failure<TenantApprovalResponse>(approvalResult.Error);

        var approvalRequest = approvalResult.Value;
        _tenantApprovalRequestRepo.Add(approvalRequest);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new TenantApprovalResponse(
            approvalRequest.Id,
            approvalRequest.Status,
            "Waiting for approval",
            approvalRequest.ExpiresAt));
    }

    public async Task<Result<IReadOnlyList<PendingTenantApprovalResponse>>> GetPendingTenantRequestsAsync(CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.IsSuperAdmin)
            return Result.Failure<IReadOnlyList<PendingTenantApprovalResponse>>(TenantApprovalRequestErrors.Unauthorized);

        var requests = await _tenantApprovalRequestRepo.GetPendingAsync(cancellationToken);
        var responses = new List<PendingTenantApprovalResponse>(requests.Count);

        foreach (var request in requests)
        {
            var requester = await _accountRepo.GetByIdAsync(request.RequestedById, cancellationToken);
            responses.Add(new PendingTenantApprovalResponse(
                request.Id,
                request.TenantCode,
                request.Name,
                request.CompanyName,
                request.TaxCode,
                requester?.Email,
                request.EmployeeCount,
                request.CreatedAt,
                request.ExpiresAt,
                request.Status));
        }

        return Result.Success<IReadOnlyList<PendingTenantApprovalResponse>>(responses);
    }

    public async Task<Result<TenantApprovalDecisionResponse>> ApproveTenantAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.IsSuperAdmin)
            return Result.Failure<TenantApprovalDecisionResponse>(TenantApprovalRequestErrors.Unauthorized);

        var approvalRequest = await _tenantApprovalRequestRepo.GetByIdForUpdateAsync(requestId, cancellationToken);
        if (approvalRequest == null)
            return Result.Failure<TenantApprovalDecisionResponse>(TenantApprovalRequestErrors.NotFound);

        if (await _tenantApprovalRequestRepo.IsTenantCodeBlockedAsync(approvalRequest.TenantCode, DateTime.UtcNow, cancellationToken))
            return Result.Failure<TenantApprovalDecisionResponse>(TenantErrors.CodeBlocked);

        if (await _tenantRepo.ExistsByCodeAsync(approvalRequest.TenantCode, cancellationToken))
            return Result.Failure<TenantApprovalDecisionResponse>(TenantErrors.CodeAlreadyExists);

        if (await _membershipRepo.ExistsOwnerByAccountIdAsync(approvalRequest.RequestedById, cancellationToken))
            return Result.Failure<TenantApprovalDecisionResponse>(TenantErrors.UserAlreadyHasTenant);

        var tenantResult = Tenant.Create(
            approvalRequest.Name,
            approvalRequest.TenantCode,
            TenancyModel.Isolated,
            approvalRequest.Currency,
            approvalRequest.CompanyName,
            approvalRequest.TaxCode);

        if (tenantResult.IsFailure)
            return Result.Failure<TenantApprovalDecisionResponse>(tenantResult.Error);

        var tenant = tenantResult.Value;
        var membershipResult = TenantMembership.Create(
            approvalRequest.RequestedById,
            tenant.Id,
            RoleType.TenantAdmin,
            isOwner: true);

        if (membershipResult.IsFailure)
            return Result.Failure<TenantApprovalDecisionResponse>(membershipResult.Error);

        var approveResult = approvalRequest.Approve();
        if (approveResult.IsFailure)
            return Result.Failure<TenantApprovalDecisionResponse>(approveResult.Error);

        _tenantRepo.Add(tenant);
        _membershipRepo.Add(membershipResult.Value);
        _tenantApprovalRequestRepo.Update(approvalRequest);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new TenantApprovalDecisionResponse(
            approvalRequest.Id,
            approvalRequest.Status,
            tenant.Id,
            tenant.TenantCode,
            tenant.Name));
    }

    public async Task<Result<TenantApprovalDecisionResponse>> RejectTenantAsync(Guid requestId, string reason, CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.IsSuperAdmin)
            return Result.Failure<TenantApprovalDecisionResponse>(TenantApprovalRequestErrors.Unauthorized);

        var approvalRequest = await _tenantApprovalRequestRepo.GetByIdForUpdateAsync(requestId, cancellationToken);
        if (approvalRequest == null)
            return Result.Failure<TenantApprovalDecisionResponse>(TenantApprovalRequestErrors.NotFound);

        var rejectResult = approvalRequest.Reject(reason);
        if (rejectResult.IsFailure)
            return Result.Failure<TenantApprovalDecisionResponse>(rejectResult.Error);

        _tenantApprovalRequestRepo.Update(approvalRequest);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new TenantApprovalDecisionResponse(
            approvalRequest.Id,
            approvalRequest.Status,
            null,
            null,
            approvalRequest.Name));
    }

    public async Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        return await _mediator.Send(new RefreshTokenCommand(request.RefreshToken), cancellationToken);
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

        if (!_currentTenant.IsSuperAdmin)
        {
            if (!_currentTenant.MembershipId.HasValue)
                return Result.Failure<AuthResponse>(AccountErrors.Unauthorized);

            if (currentRefreshToken.MembershipId != _currentTenant.MembershipId.Value)
                return Result.Failure<AuthResponse>(AccountErrors.Unauthorized);
        }

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
            {
                await _rateLimiter.RecordFailureAsync(request.ClientIp, invitation.Email, invitation.IdTenant);
                return Result.Failure<AuthResponse>(AccountErrors.InvalidCurrentPassword);
            }

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
            if (!defaultDepartment.IsActive)
                return Result.Failure<AuthResponse>(DepartmentErrors.Inactive);

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
            new TenantMembershipSummary(membership.Id, membership.AccountId, membership.IdTenant, membership.Role, membership.IsOwner, membership.IsActive, membership.CreatedAt),
            accessToken,
            refreshTokenRaw));
    }

    public async Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        return await _mediator.Send(
            new ChangePasswordCommand(request.AccountId, request.CurrentPassword, request.NewPassword),
            cancellationToken);
    }

    public async Task<Result> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        return await _mediator.Send(new LogoutCommand(refreshToken), cancellationToken);
    }
}
