using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Auth.Support;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Membership.DTOs.Requests;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.Invitations;
using FinFlow.Domain.RefreshTokens;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Tenants;

namespace FinFlow.Application.Membership.Commands.AcceptInvite;

public sealed class AcceptInviteCommandHandler : MediatR.IRequestHandler<AcceptInviteCommand, Result<AuthResponse>>
{
    private readonly IInvitationRepository _invitationRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILoginRateLimiter _rateLimiter;
    private readonly ICurrentTenant _currentTenant;

    public AcceptInviteCommandHandler(
        IInvitationRepository invitationRepository,
        ITenantRepository tenantRepository,
        IAccountRepository accountRepository,
        ITenantMembershipRepository membershipRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        IPasswordHasher passwordHasher,
        ILoginRateLimiter rateLimiter,
        ICurrentTenant currentTenant)
    {
        _invitationRepository = invitationRepository;
        _tenantRepository = tenantRepository;
        _accountRepository = accountRepository;
        _membershipRepository = membershipRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _rateLimiter = rateLimiter;
        _currentTenant = currentTenant;
    }

    public async Task<Result<AuthResponse>> Handle(AcceptInviteCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        
        var invitation = await _invitationRepository.GetByTokenForUpdateAsync(request.InviteToken, cancellationToken);
        if (invitation == null)
            return Result.Failure<AuthResponse>(InvitationErrors.NotFound);

        var tenant = await _tenantRepository.GetByIdAsync(invitation.IdTenant, cancellationToken);
        if (tenant == null || !tenant.IsActive)
            return Result.Failure<AuthResponse>(TenantErrors.NotFound);

        var existingAccount = await _accountRepository.GetLoginInfoByEmailAsync(invitation.Email, cancellationToken);
        AccountLoginInfo accountInfo;
        Guid accountId;

        if (existingAccount != null)
        {
            if (!existingAccount.IsActive)
                return Result.Failure<AuthResponse>(AccountErrors.AlreadyDeactivated);

            if (!_passwordHasher.VerifyPassword(request.Password, existingAccount.PasswordHash))
            {
                await _rateLimiter.RecordFailureAsync(request.ClientIp, invitation.Email, invitation.IdTenant);
                return Result.Failure<AuthResponse>(AccountErrors.InvalidCurrentPassword);
            }

            var alreadyMember = await _membershipRepository.ExistsAsync(existingAccount.Id, invitation.IdTenant, cancellationToken);
            if (alreadyMember)
                return Result.Failure<AuthResponse>(InvitationErrors.AlreadyMember);

            accountInfo = existingAccount;
            accountId = existingAccount.Id;
        }
        else
        {
            if (!PasswordRules.IsStrong(request.Password))
                return Result.Failure<AuthResponse>(AccountErrors.PasswordTooWeak);

            var createAccountResult = Account.Create(
                invitation.Email,
                _passwordHasher.HashPassword(request.Password));

            if (createAccountResult.IsFailure)
                return Result.Failure<AuthResponse>(createAccountResult.Error);

            var account = createAccountResult.Value;
            var verifyResult = account.MarkEmailVerified(DateTime.UtcNow);
            if (verifyResult.IsFailure)
                return Result.Failure<AuthResponse>(verifyResult.Error);

            _accountRepository.Add(account);
            accountInfo = new AccountLoginInfo(account.Id, account.Email, account.PasswordHash, account.IsActive, account.IsEmailVerified, account.EmailVerifiedAt);
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

            _membershipRepository.Add(membership);
            _invitationRepository.Update(invitation);
            _refreshTokenRepository.Add(refreshTokenResult.Value);
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

        return Result.Success(new AuthResponse(
            accessToken,
            refreshTokenRaw,
            accountInfo.Id,
            membership.Id,
            accountInfo.Email,
            membership.Role,
            membership.IdTenant));
    }
}
