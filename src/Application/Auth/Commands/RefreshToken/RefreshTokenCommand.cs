using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Auth.Commands.RefreshToken;

public record RefreshTokenCommand(RefreshTokenRequest Request) : ICommand<Result<AuthResponse>>;
