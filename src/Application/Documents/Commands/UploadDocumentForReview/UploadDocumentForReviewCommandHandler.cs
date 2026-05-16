using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Subscriptions;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.TenantSubscriptions;
using MediatR;

namespace FinFlow.Application.Documents.Commands.UploadDocumentForReview;

public sealed class UploadDocumentForReviewCommandHandler
    : IRequestHandler<UploadDocumentForReviewCommand, Result<DocumentOcrDraftResponse>>
{
    private static readonly HashSet<string> SupportedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/webp"
    };

    private const int MaxFileSizeBytes = 10 * 1024 * 1024;

private readonly IUploadedDocumentDraftRepository _uploadedDocumentDraftRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOcrExtractionService _ocrExtractionService;
    private readonly ISubscriptionQuotaGate _subscriptionQuotaGate;
    private readonly IDocumentStorageProvider _documentStorageProvider;

    public UploadDocumentForReviewCommandHandler(
        IUploadedDocumentDraftRepository uploadedDocumentDraftRepository,
        IUnitOfWork unitOfWork,
        IOcrExtractionService ocrExtractionService,
        ISubscriptionQuotaGate subscriptionQuotaGate,
        IDocumentStorageProvider documentStorageProvider)
    {
        _uploadedDocumentDraftRepository = uploadedDocumentDraftRepository;
        _unitOfWork = unitOfWork;
        _ocrExtractionService = ocrExtractionService;
        _subscriptionQuotaGate = subscriptionQuotaGate;
        _documentStorageProvider = documentStorageProvider;
    }

    public async Task<Result<DocumentOcrDraftResponse>> Handle(UploadDocumentForReviewCommand request, CancellationToken cancellationToken)
    {
        var fileName = request.FileName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
            return Result.Failure<DocumentOcrDraftResponse>(UploadedDocumentDraftErrors.FileNameRequired);

        if (fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            return Result.Failure<DocumentOcrDraftResponse>(UploadedDocumentDraftErrors.InvalidFileName);

var contentType = request.ContentType?.Trim() ?? string.Empty;
        if (!SupportedContentTypes.Contains(contentType))
            return Result.Failure<DocumentOcrDraftResponse>(UploadedDocumentDraftErrors.UnsupportedContentType);

        if (request.FileContents.Length > MaxFileSizeBytes)
            return Result.Failure<DocumentOcrDraftResponse>(UploadedDocumentDraftErrors.FileTooLarge);

        var pageCountResult = await _ocrExtractionService.GetPageCountAsync(
            contentType,
            request.FileContents,
            cancellationToken);
        if (pageCountResult.IsFailure)
            return Result.Failure<DocumentOcrDraftResponse>(pageCountResult.Error);

        var processedPageCount = pageCountResult.Value;

        var quotaResult = await _subscriptionQuotaGate.EnsureOcrAllowedAsync(
            request.TenantId,
            request.MembershipId,
            processedPageCount,
            cancellationToken);
        if (quotaResult.IsFailure)
            return Result.Failure<DocumentOcrDraftResponse>(quotaResult.Error);

        var extractionResult = await _ocrExtractionService.ExtractAsync(
            fileName,
            contentType,
            request.FileContents,
            cancellationToken);

        if (extractionResult.IsFailure)
            return Result.Failure<DocumentOcrDraftResponse>(extractionResult.Error);

        var extracted = extractionResult.Value;

        var validationResult = ValidateOcrPayload(extracted);
        if (validationResult.IsFailure)
            return Result.Failure<DocumentOcrDraftResponse>(validationResult.Error);

        var lineItems = extracted.LineItems
            .Select(item => new DocumentOcrDraftLineItemResponse(item.ItemName, item.Quantity, item.UnitPrice, item.Total))
            .ToList();

        var documentId = Guid.NewGuid();
        var uploadedAtUtc = DateTime.UtcNow;

        var draftLineItemsResult = lineItems
            .Select(item => UploadedDocumentDraftLineItem.Create(item.ItemName, item.Quantity, item.UnitPrice, item.Total))
            .ToList();

        var firstFailure = draftLineItemsResult.FirstOrDefault(r => r.IsFailure);
        if (firstFailure is not null)
            return Result.Failure<DocumentOcrDraftResponse>(firstFailure.Error);

        var draftLineItems = draftLineItemsResult.Select(r => r.Value).ToList();

var draftResult = UploadedDocumentDraft.CreateSuggested(
            documentId,
            request.TenantId,
            request.MembershipId,
            fileName,
            contentType,
            extracted.VendorName,
            extracted.Reference,
            extracted.DocumentDate,
            extracted.Category,
            extracted.VendorTaxId,
            extracted.Subtotal,
            extracted.Vat,
            extracted.TotalAmount,
            extracted.Source,
            request.Email,
            extracted.ConfidenceLabel,
            uploadedAtUtc,
            contentType,
            request.FileContents,
            draftLineItems);

        if (draftResult.IsFailure)
            return Result.Failure<DocumentOcrDraftResponse>(draftResult.Error);

        _uploadedDocumentDraftRepository.Add(draftResult.Value);
        await _subscriptionQuotaGate.RecordOcrUsageAsync(quotaResult.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var draft = draftResult.Value;
        var response = new DocumentOcrDraftResponse(
            draft.Id,
            draft.OriginalFileName,
            draft.ContentType,
            draft.VendorName,
            draft.Reference,
            draft.DocumentDate,
            draft.Category,
            draft.VendorTaxId ?? string.Empty,
            draft.Subtotal,
            draft.Vat,
            draft.TotalAmount,
            draft.Source,
            draft.UploadedByStaff,
            draft.ConfidenceLabel,
            draft.HasImage,
            draft.LineItems
                .Select(item => new DocumentOcrDraftLineItemResponse(item.ItemName, item.Quantity, item.UnitPrice, item.Total))
                .ToList());

        return Result.Success(response);
    }

    private static Result ValidateOcrPayload(OcrExtractionResult extracted)
    {
        if (extracted.LineItems is null || extracted.LineItems.Count == 0)
            return Result.Failure(UploadedDocumentDraftErrors.LineItemRequired);

        foreach (var lineItem in extracted.LineItems)
        {
            if (string.IsNullOrWhiteSpace(lineItem.ItemName))
                return Result.Failure(UploadedDocumentDraftErrors.LineItemNameRequired);

            if (lineItem.Quantity <= 0)
                return Result.Failure(UploadedDocumentDraftErrors.LineItemQuantityInvalid);

            if (lineItem.UnitPrice <= 0)
                return Result.Failure(UploadedDocumentDraftErrors.LineItemUnitPriceInvalid);

            if (lineItem.Total <= 0)
                return Result.Failure(UploadedDocumentDraftErrors.LineItemTotalInvalid);
        }

        return Result.Success();
    }
}
