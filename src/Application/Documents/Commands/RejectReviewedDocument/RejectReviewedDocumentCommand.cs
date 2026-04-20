using FinFlow.Application.Common;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Documents.Commands.RejectReviewedDocument;

public sealed record RejectReviewedDocumentCommand(Guid DocumentId, Guid TenantId, Guid MembershipId, string Reason) : ICommand<Result<ReviewedDocumentResponse>>;
