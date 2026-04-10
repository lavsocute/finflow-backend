using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Auth.Queries.GetCurrentWorkspace;

public record GetCurrentWorkspaceQuery(Guid AccountId) : IQuery<Result<CurrentWorkspaceResponse>>;
