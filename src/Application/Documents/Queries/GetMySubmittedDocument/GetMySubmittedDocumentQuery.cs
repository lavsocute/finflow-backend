using FinFlow.Application.Common;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Documents.Queries.GetMySubmittedDocument;

public sealed record GetMySubmittedDocumentQuery(
    Guid TenantId,
    Guid MembershipId,
    Guid DocumentId
) : IQuery<Result<MySubmittedDocumentDetailResponse>>;