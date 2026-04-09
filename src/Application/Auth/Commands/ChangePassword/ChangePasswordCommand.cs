using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Auth.Commands.ChangePassword;

public record ChangePasswordCommand(ChangePasswordRequest Request) : ICommand<Result>;
