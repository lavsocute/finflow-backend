using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Auth.Commands.Logout;

public record LogoutCommand(LogoutRequest Request) : ICommand<Result>;
