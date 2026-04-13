using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Auth.Commands.ResetPasswordByToken;

public sealed record ResetPasswordByTokenCommand(ResetPasswordByTokenRequest Request) : Common.ICommand<Result>;
