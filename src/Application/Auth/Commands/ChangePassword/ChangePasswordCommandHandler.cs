using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Auth.Support;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Entities;
using FinFlow.Domain.RefreshTokens;

namespace FinFlow.Application.Auth.Commands.ChangePassword;

public sealed class ChangePasswordCommandHandler : MediatR.IRequestHandler<ChangePasswordCommand, Result>
{
    private readonly IAccountRepository _accountRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;

    public ChangePasswordCommandHandler(
        IAccountRepository accountRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher)
    {
        _accountRepository = accountRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
    }

    public async Task<Result> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        
        var accountInfo = await _accountRepository.GetLoginInfoByIdAsync(request.AccountId, cancellationToken);
        if (accountInfo == null)
            return Result.Failure(AccountErrors.NotFound);
        if (!accountInfo.IsActive)
            return Result.Failure(AccountErrors.AlreadyDeactivated);
        if (!_passwordHasher.VerifyPassword(request.CurrentPassword, accountInfo.PasswordHash))
            return Result.Failure(AccountErrors.InvalidCurrentPassword);
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            return Result.Failure(AccountErrors.PasswordTooShort);
        if (!PasswordRules.IsStrong(request.NewPassword))
            return Result.Failure(AccountErrors.PasswordTooWeak);

        var account = await _accountRepository.GetByIdForUpdateAsync(accountInfo.Id, cancellationToken);
        if (account == null)
            return Result.Failure(AccountErrors.NotFound);

        var changeResult = account.ChangePassword(_passwordHasher.HashPassword(request.NewPassword));
        if (changeResult.IsFailure)
            return changeResult;

        await _refreshTokenRepository.RevokeAllForAccountAsync(account.Id, "Password changed", cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
