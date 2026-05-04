using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.Documents.Queries.GetApprovalDetail;

public sealed record GetApprovalDetailQuery(Guid TenantId, Guid DocumentId) : IRequest<Result<ApprovalDetailResponse>>;