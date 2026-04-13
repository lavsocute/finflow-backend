using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Auth.Commands.ResetPasswordByOtp;

public sealed record ResetPasswordByOtpCommand(ResetPasswordByOtpRequest Request) : Common.ICommand<Result>;
