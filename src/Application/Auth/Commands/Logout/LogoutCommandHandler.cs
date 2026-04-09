using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.RefreshTokens;

namespace FinFlow.Application.Auth.Commands.Logout;

public sealed class LogoutCommandHandler : MediatR.IRequestHandler<LogoutCommand, Result>
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;

    public LogoutCommandHandler(IRefreshTokenRepository refreshTokenRepository)
    {
        _refreshTokenRepository = refreshTokenRepository;
    }

    public async Task<Result> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        
        var revoked = await _refreshTokenRepository.RevokeByTokenAsync(request.RefreshToken, "User logout", cancellationToken);
        if (!revoked)
            return Result.Failure(RefreshTokenErrors.NotFound);

        return Result.Success();
    }
}
