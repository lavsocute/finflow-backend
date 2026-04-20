using FinFlow.Application.Common;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Documents.Commands.ApproveReviewedDocument;

public sealed record ApproveReviewedDocumentCommand(Guid DocumentId, Guid TenantId, Guid MembershipId) : ICommand<Result<ReviewedDocumentResponse>>;
