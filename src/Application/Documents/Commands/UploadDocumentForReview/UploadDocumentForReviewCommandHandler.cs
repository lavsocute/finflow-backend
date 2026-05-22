using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Common.ExchangeRates;
using FinFlow.Application.Subscriptions;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.TenantSubscriptions;
using FinFlow.Domain.Tenants;
using MediatR;
using Microsoft.Extensions.Logging;

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
    private const string DefaultBaseCurrency = "VND";

private readonly IUploadedDocumentDraftRepository _uploadedDocumentDraftRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOcrExtractionService _ocrExtractionService;
    private readonly ISubscriptionQuotaGate _subscriptionQuotaGate;
    private readonly IDocumentStorageProvider _documentStorageProvider;
    private readonly ITenantRepository _tenantRepository;
    private readonly IExchangeRateService _exchangeRateService;
    private readonly ILogger<UploadDocumentForReviewCommandHandler> _logger;

    public UploadDocumentForReviewCommandHandler(
        IUploadedDocumentDraftRepository uploadedDocumentDraftRepository,
        IUnitOfWork unitOfWork,
        IOcrExtractionService ocrExtractionService,
        ISubscriptionQuotaGate subscriptionQuotaGate,
        IDocumentStorageProvider documentStorageProvider,
        ITenantRepository tenantRepository,
        IExchangeRateService exchangeRateService,
        ILogger<UploadDocumentForReviewCommandHandler> logger)
    {
        _uploadedDocumentDraftRepository = uploadedDocumentDraftRepository;
        _unitOfWork = unitOfWork;
        _ocrExtractionService = ocrExtractionService;
        _subscriptionQuotaGate = subscriptionQuotaGate;
        _documentStorageProvider = documentStorageProvider;
        _tenantRepository = tenantRepository;
        _exchangeRateService = exchangeRateService;
        _logger = logger;
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

        // Fix #8: charge quota using actual processed page count from extraction
        // (post-truncation), not the pre-extraction page count. Adjust quota decision
        // so the recorded amount matches what was actually OCRed.
        if (extracted.ProcessedPageCount > 0 && extracted.ProcessedPageCount < processedPageCount)
        {
            quotaResult = Result.Success(quotaResult.Value with { ApprovedUnitCount = extracted.ProcessedPageCount });
        }

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

        // Multi-currency snapshot: resolve tenant base currency, infer document currency
        // from OCR (fallback to base when null), fetch rate, and store on the draft.
        var draft = draftResult.Value;

        var tenantSummary = await _tenantRepository.GetByIdAsync(request.TenantId, cancellationToken);
        var baseCurrency = string.IsNullOrWhiteSpace(tenantSummary?.Currency) ? DefaultBaseCurrency : tenantSummary!.Currency;
        var documentCurrency = string.IsNullOrWhiteSpace(extracted.CurrencyCode) ? baseCurrency : extracted.CurrencyCode!;

        decimal exchangeRate = 1m;
        if (!string.Equals(documentCurrency, baseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            var rateResult = await _exchangeRateService.GetRateAsync(
                documentCurrency, baseCurrency, extracted.DocumentDate, cancellationToken);

            if (rateResult.IsSuccess)
            {
                exchangeRate = rateResult.Value.Rate;
            }
            else
            {
                _logger.LogWarning(
                    "Exchange rate {From}->{To} on {Date} unavailable; defaulting draft to base currency. {Error}",
                    documentCurrency, baseCurrency, extracted.DocumentDate, rateResult.Error.Description);
                // Fail-safe: store as base currency so the document can still be reviewed.
                // Reviewer can manually correct currency before submitting.
                documentCurrency = baseCurrency;
                exchangeRate = 1m;
            }
        }

        var currencyContext = draft.SetCurrencyContext(documentCurrency, baseCurrency, exchangeRate);
        if (currencyContext.IsFailure)
            return Result.Failure<DocumentOcrDraftResponse>(currencyContext.Error);

        _uploadedDocumentDraftRepository.Add(draft);
        await _subscriptionQuotaGate.RecordOcrUsageAsync(quotaResult.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

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
                .ToList(),
            draft.CurrencyCode,
            draft.ExchangeRate,
            draft.BaseCurrencyCode,
            decimal.Round(draft.TotalAmount * draft.ExchangeRate, 2, MidpointRounding.AwayFromZero),
            quotaResult.Value.ApprovedUnitCount);

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
