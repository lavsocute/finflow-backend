using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using MediatR;

namespace FinFlow.Application.Documents.Queries.ExportApprovalQueue;

public sealed record ExportApprovalQueueQuery(
    Guid TenantId,
    ApprovalStatusFilter Status = ApprovalStatusFilter.All,
    string? Search = null) : IRequest<Result<ExportApprovalQueueResponse>>;