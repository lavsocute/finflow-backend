using FinFlow.Application.Common;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Documents.Queries.GetMySubmittedDocuments;

public sealed record GetMySubmittedDocumentsQuery(Guid TenantId, Guid MembershipId)
    : IQuery<Result<IReadOnlyList<MySubmittedDocumentSummaryResponse>>>;
