using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Common;
using FinFlow.Application.Membership.DTOs.Requests;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Membership.Commands.AcceptInvite;

public record AcceptInviteCommand(AcceptInviteRequest Request)
    : ICommand<Result<AuthResponse>>;
