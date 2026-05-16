using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.Documents.Commands.WithdrawReviewedDocument;

public sealed record WithdrawReviewedDocumentCommand(
    Guid DocumentId,
    Guid TenantId,
    Guid MembershipId,
    bool IsTenantOwner) : IRequest<Result<ReviewedDocumentResponse>>;
