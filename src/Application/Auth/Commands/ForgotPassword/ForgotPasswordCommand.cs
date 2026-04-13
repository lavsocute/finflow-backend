using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Auth.Commands.ForgotPassword;

public sealed record ForgotPasswordCommand(ForgotPasswordRequest Request) : Common.ICommand<Result<ChallengeDispatchResponse>>;
