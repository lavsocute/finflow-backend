using FinFlow.Application.Common;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Documents.Queries.GetMyDocumentDrafts;

public sealed record GetMyDocumentDraftsQuery(Guid TenantId, Guid MembershipId)
    : IQuery<Result<IReadOnlyList<MyDocumentDraftSummaryResponse>>>;
