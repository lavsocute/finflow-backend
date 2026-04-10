using FinFlow.Application.Auth.DTOs.Responses;
using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Auth.Queries.GetMyWorkspaces;

public sealed record GetMyWorkspacesQuery(Guid AccountId)
    : IQuery<Result<IReadOnlyList<MyWorkspaceResponse>>>;
