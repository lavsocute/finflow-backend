using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Entities;
using FinFlow.Domain.RefreshTokens;
using FinFlow.Domain.TenantMemberships;

namespace FinFlow.Application.Auth.Commands.RefreshToken;

public sealed class RefreshTokenCommandHandler : MediatR.IRequestHandler<RefreshTokenCommand, Result<AuthResponse>>
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;

    public RefreshTokenCommandHandler(
        IRefreshTokenRepository refreshTokenRepository,
        IAccountRepository accountRepository,
        ITenantMembershipRepository membershipRepository,
        IUnitOfWork unitOfWork,
        ITokenService tokenService)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _accountRepository = accountRepository;
        _membershipRepository = membershipRepository;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
    }

    public async Task<Result<AuthResponse>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        
        var storedToken = await _refreshTokenRepository.GetByTokenAsync(request.RefreshToken, cancellationToken);
        if (storedToken == null)
            return Result.Failure<AuthResponse>(RefreshTokenErrors.NotFound);

        if (!storedToken.IsActive)
        {
            return storedToken.IsRevoked
                ? Result.Failure<AuthResponse>(RefreshTokenErrors.Revoked)
                : Result.Failure<AuthResponse>(RefreshTokenErrors.Expired);
        }

        var account = await _accountRepository.GetLoginInfoByIdAsync(storedToken.AccountId, cancellationToken);
        if (account == null || !account.IsActive)
            return Result.Failure<AuthResponse>(AccountErrors.AlreadyDeactivated);

        var membership = await _membershipRepository.GetByIdAsync(storedToken.MembershipId, cancellationToken);
        if (membership == null || !membership.IsActive)
            return Result.Failure<AuthResponse>(TenantMembershipErrors.NotFound);

        var newRawToken = _tokenService.GenerateRefreshToken();
        var replaceResult = storedToken.ReplaceWith(newRawToken, _tokenService.RefreshTokenExpirationDays);
        if (replaceResult.IsFailure)
            return Result.Failure<AuthResponse>(replaceResult.Error);

        var (newRefreshTokenEntity, rawTokenForClient) = replaceResult.Value;
        _refreshTokenRepository.Add(newRefreshTokenEntity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(
            account.Id,
            account.Email,
            membership.Role.ToString(),
            membership.IdTenant,
            membership.Id);

        return Result.Success(new AuthResponse(
            accessToken,
            rawTokenForClient,
            account.Id,
            membership.Id,
            account.Email,
            membership.Role,
            membership.IdTenant));
    }
}
