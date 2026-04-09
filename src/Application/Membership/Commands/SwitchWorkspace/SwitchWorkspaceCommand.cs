using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Common;
using FinFlow.Application.Membership.DTOs.Requests;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Membership.Commands.SwitchWorkspace;

public record SwitchWorkspaceCommand(SwitchWorkspaceRequest Request)
    : ICommand<Result<AuthResponse>>;
