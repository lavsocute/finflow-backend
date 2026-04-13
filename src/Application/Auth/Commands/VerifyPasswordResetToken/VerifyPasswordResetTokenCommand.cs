using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Auth.Commands.VerifyPasswordResetToken;

public sealed record VerifyPasswordResetTokenCommand(string Token) : Common.ICommand<Result<bool>>;
