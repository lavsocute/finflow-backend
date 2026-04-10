using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.RefreshTokens;

namespace FinFlow.Application.Auth.Commands.Logout;

public sealed class LogoutCommandHandler : MediatR.IRequestHandler<LogoutCommand, Result>
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUnitOfWork _unitOfWork;

    public LogoutCommandHandler(
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        
        var revoked = await _refreshTokenRepository.RevokeByTokenAsync(request.RefreshToken, "User logout", cancellationToken);
        if (!revoked)
            return Result.Failure(RefreshTokenErrors.NotFound);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
