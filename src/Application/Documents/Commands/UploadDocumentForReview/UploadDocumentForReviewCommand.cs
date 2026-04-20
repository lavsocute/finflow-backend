using FinFlow.Application.Common;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Documents.Commands.UploadDocumentForReview;

public sealed record UploadDocumentForReviewCommand(
    Guid AccountId,
    Guid TenantId,
    Guid MembershipId,
    string Email,
    string FileName,
    string ContentType,
    byte[] FileContents
) : ICommand<Result<DocumentOcrDraftResponse>>;
