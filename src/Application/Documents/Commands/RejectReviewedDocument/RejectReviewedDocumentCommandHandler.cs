using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using MediatR;

namespace FinFlow.Application.Documents.Commands.RejectReviewedDocument;

public sealed class RejectReviewedDocumentCommandHandler
    : IRequestHandler<RejectReviewedDocumentCommand, Result<ReviewedDocumentResponse>>
{
    private readonly IReviewedDocumentRepository _reviewedDocumentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RejectReviewedDocumentCommandHandler(
        IReviewedDocumentRepository reviewedDocumentRepository,
        IUnitOfWork unitOfWork)
    {
        _reviewedDocumentRepository = reviewedDocumentRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ReviewedDocumentResponse>> Handle(RejectReviewedDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await _reviewedDocumentRepository.GetByIdForUpdateAsync(request.DocumentId, request.TenantId, cancellationToken);
        if (document == null)
            return Result.Failure<ReviewedDocumentResponse>(ReviewedDocumentErrors.NotFound);

        if (document.MembershipId == request.MembershipId)
            return Result.Failure<ReviewedDocumentResponse>(ReviewedDocumentErrors.SelfApprovalNotAllowed);

        var rejectResult = document.Reject(request.Reason);
        if (rejectResult.IsFailure)
            return Result.Failure<ReviewedDocumentResponse>(rejectResult.Error);

        _reviewedDocumentRepository.Update(document);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new ReviewedDocumentResponse(
            document.Id,
            document.Status.ToString(),
            document.SubmittedAt,
            document.VendorName,
            document.Reference,
            document.TotalAmount,
            document.DueDate,
            document.ReviewedByStaff));
    }
}
