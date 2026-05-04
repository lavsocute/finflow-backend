using FinFlow.Application.Common;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;

namespace FinFlow.Application.Documents.Queries.GetApprovalQueue;

public sealed record GetApprovalQueueQuery(
    Guid TenantId,
    ApprovalStatusFilter Status = ApprovalStatusFilter.All,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IQuery<Result<ApprovalQueueResponse>>;