using FinFlow.Application.Common;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Documents.Queries.GetMyDocumentDraft;

public sealed record GetMyDocumentDraftQuery(Guid TenantId, Guid MembershipId, Guid DocumentId)
    : IQuery<Result<DocumentOcrDraftResponse>>;
