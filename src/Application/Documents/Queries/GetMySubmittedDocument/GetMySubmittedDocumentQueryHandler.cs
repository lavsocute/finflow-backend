using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using MediatR;

namespace FinFlow.Application.Documents.Queries.GetMySubmittedDocument;

public sealed class GetMySubmittedDocumentQueryHandler
    : IRequestHandler<GetMySubmittedDocumentQuery, Result<MySubmittedDocumentDetailResponse>>
{
    private readonly IReviewedDocumentRepository _reviewedDocumentRepository;
    private readonly IUploadedDocumentDraftRepository _uploadedDocumentDraftRepository;

    public GetMySubmittedDocumentQueryHandler(
        IReviewedDocumentRepository reviewedDocumentRepository,
        IUploadedDocumentDraftRepository uploadedDocumentDraftRepository)
    {
        _reviewedDocumentRepository = reviewedDocumentRepository;
        _uploadedDocumentDraftRepository = uploadedDocumentDraftRepository;
    }

    public async Task<Result<MySubmittedDocumentDetailResponse>> Handle(GetMySubmittedDocumentQuery request, CancellationToken cancellationToken)
    {
        var document = await _reviewedDocumentRepository.GetOwnedByIdAsync(
            request.DocumentId,
            request.TenantId,
            request.MembershipId,
            cancellationToken);

        if (document == null)
            return Result.Failure<MySubmittedDocumentDetailResponse>(ReviewedDocumentErrors.NotFound);

        var originalDraft = await _uploadedDocumentDraftRepository.GetByIdAsync(
            request.DocumentId,
            request.TenantId,
            request.MembershipId,
            includeInactive: true,
            cancellationToken);

        var previewImageDataUrl = BuildPreviewImageDataUrl(originalDraft?.ImageContentType, originalDraft?.ImageData);

        return Result.Success(new MySubmittedDocumentDetailResponse(
            document.Id,
            document.OriginalFileName,
            document.ContentType,
            previewImageDataUrl is not null,
            previewImageDataUrl,
            document.VendorName,
            document.Reference,
            document.DocumentDate,
            document.Category,
            document.VendorTaxId ?? string.Empty,
            document.Subtotal,
            document.Vat,
            document.TotalAmount,
            document.CurrencyCode,
            document.ExchangeRate,
            document.BaseCurrencyCode,
            document.TotalAmountInBaseCurrency,
            document.Source,
            ToStatusString(document),
            document.ReviewedByStaff,
            document.SubmittedAt,
            document.UpdatedAt,
            document.RejectionReason,
            document.LineItems
                .Select(item => new MySubmittedDocumentDetailLineItemResponse(
                    item.ItemName,
                    item.Quantity,
                    item.UnitPrice,
                    item.DiscountPercent,
                    item.DiscountAmount,
                    item.TaxRate,
                    item.TaxableAmount,
                    item.TaxAmount,
                    item.Total))
                .ToList(),
            document.TaxLines
                .Select(item => new DocumentTaxLineResponse(
                    item.TaxType,
                    item.Rate,
                    item.TaxableAmount,
                    item.TaxAmount))
                .ToList()));
    }

    private static string? BuildPreviewImageDataUrl(string? contentType, byte[]? imageData)
    {
        if (imageData is not { Length: > 0 } || string.IsNullOrWhiteSpace(contentType))
            return null;

        return $"data:{contentType};base64,{Convert.ToBase64String(imageData)}";
    }

    private static string ToStatusString(ReviewedDocument document) =>
        document.Status switch
        {
            ReviewedDocumentStatus.ReadyForApproval => "Submitted",
            ReviewedDocumentStatus.Approved => "Approved",
            ReviewedDocumentStatus.Rejected => "Rejected",
            _ => document.Status.ToString()
        };
}
