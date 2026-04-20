using FinFlow.Application.Common;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Documents.Queries.GetPendingApprovalItems;

public sealed record GetPendingApprovalItemsQuery(Guid TenantId) : IQuery<Result<IReadOnlyList<PendingApprovalItemResponse>>>;
