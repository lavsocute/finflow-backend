using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Entities;
using FinFlow.Domain.RefreshTokens;
using FinFlow.Domain.TenantMemberships;
using RefreshTokenEntity = FinFlow.Domain.Entities.RefreshToken;

namespace FinFlow.Application.Auth.Commands.SelectWorkspace;

public sealed class SelectWorkspaceCommandHandler : MediatR.IRequestHandler<SelectWorkspaceCommand, Result<WorkspaceSessionResponse>>
{
    private const string SessionKind = "workspace";

    private readonly IAccountRepository _accountRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly IOtpOperationLockService _otpLockService;

    public SelectWorkspaceCommandHandler(
        IAccountRepository accountRepository,
        ITenantMembershipRepository membershipRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        IOtpOperationLockService otpLockService)
    {
        _accountRepository = accountRepository;
        _membershipRepository = membershipRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _otpLockService = otpLockService;
    }

    public async Task<Result<WorkspaceSessionResponse>> Handle(SelectWorkspaceCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        var account = await _accountRepository.GetLoginInfoByIdAsync(request.AccountId, cancellationToken);
        if (account == null || !account.IsActive)
            return Result.Failure<WorkspaceSessionResponse>(AccountErrors.Unauthorized);

        await using var lockHandle = await _otpLockService.AcquireLockAsync(
            $"select-workspace:{request.AccountId}",
            TimeSpan.FromSeconds(30),
            cancellationToken);

        if (lockHandle == null)
            return Result.Failure<WorkspaceSessionResponse>(AccountErrors.TooManyRequests);

        var membership = await _membershipRepository.GetByIdAsync(request.MembershipId, cancellationToken);
        if (membership == null || !membership.IsActive)
            return Result.Failure<WorkspaceSessionResponse>(TenantMembershipErrors.NotFound);

        if (membership.AccountId != request.AccountId)
            return Result.Failure<WorkspaceSessionResponse>(AccountErrors.Unauthorized);

        var accessToken = _tokenService.GenerateAccessToken(
            account.Id,
            account.Email,
            membership.Role.ToString(),
            membership.IdTenant,
            membership.Id);

        var refreshTokenRaw = _tokenService.GenerateRefreshToken();
        var refreshTokenResult = RefreshTokenEntity.Create(
            refreshTokenRaw,
            account.Id,
            membership.Id,
            _tokenService.RefreshTokenExpirationDays);

        if (refreshTokenResult.IsFailure)
            return Result.Failure<WorkspaceSessionResponse>(refreshTokenResult.Error);

        _refreshTokenRepository.Add(refreshTokenResult.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new WorkspaceSessionResponse(
            accessToken,
            refreshTokenRaw,
            account.Id,
            membership.Id,
            account.Email,
            membership.Role,
            membership.IdTenant,
            SessionKind));
    }
}
