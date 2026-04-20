using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using MediatR;

namespace FinFlow.Application.Documents.Commands.SubmitReviewedDocument;

public sealed class SubmitReviewedDocumentCommandHandler
    : IRequestHandler<SubmitReviewedDocumentCommand, Result<ReviewedDocumentResponse>>
{
    private readonly IReviewedDocumentRepository _reviewedDocumentRepository;
    private readonly IUploadedDocumentDraftRepository _uploadedDocumentDraftRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SubmitReviewedDocumentCommandHandler(
        IReviewedDocumentRepository reviewedDocumentRepository,
        IUploadedDocumentDraftRepository uploadedDocumentDraftRepository,
        IUnitOfWork unitOfWork)
    {
        _reviewedDocumentRepository = reviewedDocumentRepository;
        _uploadedDocumentDraftRepository = uploadedDocumentDraftRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ReviewedDocumentResponse>> Handle(SubmitReviewedDocumentCommand request, CancellationToken cancellationToken)
    {
        var draft = await _uploadedDocumentDraftRepository.GetByIdAsync(
            request.DocumentId,
            request.TenantId,
            request.MembershipId,
            cancellationToken);
        if (draft == null)
            return Result.Failure<ReviewedDocumentResponse>(UploadedDocumentDraftErrors.NotFound);

        var lineItems = request.LineItems
            .Select(item => ReviewedDocumentLineItem.Create(item.ItemName, item.Quantity, item.UnitPrice, item.Total))
            .ToList();

        var documentResult = ReviewedDocument.CreateSubmitted(
            request.DocumentId,
            request.TenantId,
            request.MembershipId,
            draft.OriginalFileName,
            draft.ContentType,
            request.VendorName,
            request.Reference,
            request.DocumentDate,
            request.DueDate,
            request.Category,
            request.VendorTaxId,
            request.Subtotal,
            request.Vat,
            request.TotalAmount,
            draft.Source,
            request.ReviewedByStaff,
            string.IsNullOrWhiteSpace(request.ConfidenceLabel) ? "Staff corrected" : request.ConfidenceLabel,
            request.SubmittedAt,
            lineItems);

        if (documentResult.IsFailure)
            return Result.Failure<ReviewedDocumentResponse>(documentResult.Error);

        var markSubmittedResult = draft.MarkSubmitted();
        if (markSubmittedResult.IsFailure)
            return Result.Failure<ReviewedDocumentResponse>(markSubmittedResult.Error);

        _uploadedDocumentDraftRepository.Update(draft);
        _reviewedDocumentRepository.Add(documentResult.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new ReviewedDocumentResponse(
            documentResult.Value.Id,
            documentResult.Value.Status.ToString(),
            documentResult.Value.SubmittedAt,
            documentResult.Value.VendorName,
            documentResult.Value.Reference,
            documentResult.Value.TotalAmount,
            documentResult.Value.DueDate,
            documentResult.Value.ReviewedByStaff));
    }
}
