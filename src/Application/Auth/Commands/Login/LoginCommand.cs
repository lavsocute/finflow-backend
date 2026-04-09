using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Auth.Commands.Login;

public record LoginCommand(LoginRequest Request)
    : ICommand<Result<AuthResponse>>;
