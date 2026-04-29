using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Vendors;
using MediatR;

namespace FinFlow.Application.Documents.Commands.SubmitReviewedDocument;

public sealed class SubmitReviewedDocumentCommandHandler
    : IRequestHandler<SubmitReviewedDocumentCommand, Result<ReviewedDocumentResponse>>
{
    private readonly IReviewedDocumentRepository _reviewedDocumentRepository;
    private readonly IUploadedDocumentDraftRepository _uploadedDocumentDraftRepository;
    private readonly IVendorRepository _vendorRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SubmitReviewedDocumentCommandHandler(
        IReviewedDocumentRepository reviewedDocumentRepository,
        IUploadedDocumentDraftRepository uploadedDocumentDraftRepository,
        IVendorRepository vendorRepository,
        IUnitOfWork unitOfWork)
    {
        _reviewedDocumentRepository = reviewedDocumentRepository;
        _uploadedDocumentDraftRepository = uploadedDocumentDraftRepository;
        _vendorRepository = vendorRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ReviewedDocumentResponse>> Handle(SubmitReviewedDocumentCommand request, CancellationToken cancellationToken)
    {
        UploadedDocumentDraft? draft = null;
        Guid documentId;
        string contentType;
        string originalFileName;

        if (request.DraftId.HasValue)
        {
            draft = await _uploadedDocumentDraftRepository.GetByIdAsync(
                request.DraftId.Value,
                request.TenantId,
                request.MembershipId,
                cancellationToken);
            if (draft == null)
                return Result.Failure<ReviewedDocumentResponse>(UploadedDocumentDraftErrors.NotFound);

            documentId = draft.Id;
            contentType = draft.ContentType;
            originalFileName = draft.OriginalFileName;
        }
        else
        {
            documentId = Guid.NewGuid();
            contentType = "manual-entry";
            originalFileName = string.IsNullOrWhiteSpace(request.OriginalFileName) ? "manual-entry" : request.OriginalFileName;
        }

        if (!string.IsNullOrWhiteSpace(request.VendorTaxId))
        {
            var vendorExists = await _vendorRepository.ExistsByTaxCodeAsync(request.VendorTaxId, request.TenantId, cancellationToken);
            if (!vendorExists)
                return Result.Failure<ReviewedDocumentResponse>(VendorErrors.NotFound);
        }

        var lineItems = request.LineItems
            .Select(item => ReviewedDocumentLineItem.Create(item.ItemName, item.Quantity, item.UnitPrice, item.Total))
            .ToList();

        var documentResult = ReviewedDocument.CreateSubmitted(
            documentId,
            request.TenantId,
            request.MembershipId,
            originalFileName,
            contentType,
            request.VendorName,
            request.Reference,
            request.DocumentDate,
            request.DueDate,
            request.Category,
            request.VendorTaxId,
            request.Subtotal,
            request.Vat,
            request.TotalAmount,
            string.IsNullOrWhiteSpace(request.Source) ? "manual-submission" : request.Source,
            request.ReviewedByStaff,
            string.IsNullOrWhiteSpace(request.ConfidenceLabel) ? "Staff corrected" : request.ConfidenceLabel,
            request.SubmittedAt,
            lineItems);

        if (documentResult.IsFailure)
            return Result.Failure<ReviewedDocumentResponse>(documentResult.Error);

        if (draft != null)
        {
            var markSubmittedResult = draft.MarkSubmitted();
            if (markSubmittedResult.IsFailure)
                return Result.Failure<ReviewedDocumentResponse>(markSubmittedResult.Error);

            _uploadedDocumentDraftRepository.Update(draft);
        }

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
