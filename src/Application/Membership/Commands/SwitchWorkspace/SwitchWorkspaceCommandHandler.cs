using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.RefreshTokens;
using FinFlow.Domain.TenantMemberships;

namespace FinFlow.Application.Membership.Commands.SwitchWorkspace;

public sealed class SwitchWorkspaceCommandHandler : MediatR.IRequestHandler<SwitchWorkspaceCommand, Result<AuthResponse>>
{
    private readonly IAccountRepository _accountRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly ICurrentTenant _currentTenant;

    public SwitchWorkspaceCommandHandler(
        IAccountRepository accountRepository,
        ITenantMembershipRepository membershipRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        ICurrentTenant currentTenant)
    {
        _accountRepository = accountRepository;
        _membershipRepository = membershipRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _currentTenant = currentTenant;
    }

    public async Task<Result<AuthResponse>> Handle(SwitchWorkspaceCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        
        var account = await _accountRepository.GetLoginInfoByIdAsync(request.AccountId, cancellationToken);
        if (account == null || !account.IsActive)
            return Result.Failure<AuthResponse>(AccountErrors.AlreadyDeactivated);

        var membership = await _membershipRepository.GetByIdAsync(request.MembershipId, cancellationToken);
        if (membership == null || !membership.IsActive)
            return Result.Failure<AuthResponse>(TenantMembershipErrors.NotFound);

        if (membership.AccountId != request.AccountId)
            return Result.Failure<AuthResponse>(AccountErrors.Unauthorized);

        var currentRefreshToken = await _refreshTokenRepository.GetByTokenAsync(request.CurrentRefreshToken, cancellationToken);
        if (currentRefreshToken == null)
            return Result.Failure<AuthResponse>(RefreshTokenErrors.NotFound);

        if (!currentRefreshToken.IsActive)
        {
            return currentRefreshToken.IsRevoked
                ? Result.Failure<AuthResponse>(RefreshTokenErrors.Revoked)
                : Result.Failure<AuthResponse>(RefreshTokenErrors.Expired);
        }

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

        _refreshTokenRepository.Add(newRefreshTokenResult.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(
            account.Id,
            account.Email,
            membership.Role.ToString(),
            membership.IdTenant,
            membership.Id);

        return Result.Success(new AuthResponse(
            accessToken,
            newRefreshTokenRaw,
            account.Id,
            membership.Id,
            account.Email,
            membership.Role,
            membership.IdTenant));
    }
}
