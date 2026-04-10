using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Auth.Commands.SelectWorkspace;

public sealed record SelectWorkspaceCommand(SelectWorkspaceRequest Request)
    : ICommand<Result<WorkspaceSessionResponse>>;
