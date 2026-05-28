using FinFlow.Application.Common.ExchangeRates;
using FinFlow.Application.Documents.Duplicates;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Application.Vendors.Services;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Tenants;
using FinFlow.Domain.Vendors;
using MediatR;
using FinFlow.Application.Chat.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinFlow.Application.Documents.Commands.SubmitReviewedDocument;

public sealed class SubmitReviewedDocumentCommandHandler
    : IRequestHandler<SubmitReviewedDocumentCommand, Result<ReviewedDocumentResponse>>
{
    private const string DefaultBaseCurrency = "VND";

    private readonly IReviewedDocumentRepository _reviewedDocumentRepository;
    private readonly IUploadedDocumentDraftRepository _uploadedDocumentDraftRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IVendorLinkResolver _vendorLinkResolver;
    private readonly IDuplicateReceiptDetector _duplicateReceiptDetector;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReviewedDocumentChunkIndexer _documentChunkIndexer;
    private readonly ITenantRepository _tenantRepository;
    private readonly IExchangeRateService _exchangeRateService;
    private readonly IVendorRepository? _vendorRepository;
    private readonly ILogger<SubmitReviewedDocumentCommandHandler> _logger;

    private const int DuplicateSlidingWindowDays = 90;

    public SubmitReviewedDocumentCommandHandler(
        IReviewedDocumentRepository reviewedDocumentRepository,
        IUploadedDocumentDraftRepository uploadedDocumentDraftRepository,
        ITenantMembershipRepository membershipRepository,
        IVendorLinkResolver vendorLinkResolver,
        IDuplicateReceiptDetector duplicateReceiptDetector,
        IUnitOfWork unitOfWork,
        IReviewedDocumentChunkIndexer documentChunkIndexer,
        ITenantRepository tenantRepository,
        IExchangeRateService exchangeRateService,
        ILogger<SubmitReviewedDocumentCommandHandler> logger,
        IVendorRepository? vendorRepository = null)
    {
        _reviewedDocumentRepository = reviewedDocumentRepository;
        _uploadedDocumentDraftRepository = uploadedDocumentDraftRepository;
        _membershipRepository = membershipRepository;
        _vendorLinkResolver = vendorLinkResolver;
        _duplicateReceiptDetector = duplicateReceiptDetector;
        _unitOfWork = unitOfWork;
        _documentChunkIndexer = documentChunkIndexer;
        _tenantRepository = tenantRepository;
        _exchangeRateService = exchangeRateService;
        _logger = logger;
        _vendorRepository = vendorRepository;
    }

    public async Task<Result<ReviewedDocumentResponse>> Handle(SubmitReviewedDocumentCommand request, CancellationToken cancellationToken)
    {
        var membership = await _membershipRepository.GetByIdAsync(request.MembershipId, cancellationToken);
        if (membership is null)
            return Result.Failure<ReviewedDocumentResponse>(TenantMembershipErrors.NotFound);

        if (!membership.DepartmentId.HasValue)
            return Result.Failure<ReviewedDocumentResponse>(ReviewedDocumentErrors.DepartmentRequired);

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
            // Vendor auto-link is resolved AFTER document validation passes so we
            // don't pollute the DB with vendors for documents that fail later
            // checks (currency, line items, etc). The resolver attaches the
            // resulting vendor Id via documentEntity.LinkVendor().
        }

        var lineItems = request.LineItems
            .Select(item => ReviewedDocumentLineItem.Create(
                item.ItemName,
                item.Quantity,
                item.UnitPrice,
                item.DiscountPercent,
                item.DiscountAmount,
                item.Total,
                item.TaxRate,
                item.TaxableAmount,
                item.TaxAmount))
            .ToList();
        var requestedTaxLines = (request.TaxLines ?? [])
            .Select(item => ReviewedDocumentTaxLine.Create(item.TaxType, item.Rate, item.TaxableAmount, item.TaxAmount))
            .ToList();
        var firstTaxFailure = requestedTaxLines.FirstOrDefault(r => r.IsFailure);
        if (firstTaxFailure is not null)
            return Result.Failure<ReviewedDocumentResponse>(firstTaxFailure.Error);
        var taxLines = requestedTaxLines.Select(r => r.Value).ToList();
        if (taxLines.Count == 0 && draft is not null)
        {
            taxLines = draft.TaxLines
                .Select(item => ReviewedDocumentTaxLine.Create(item.TaxType, item.Rate, item.TaxableAmount, item.TaxAmount).Value)
                .ToList();
        }

        var documentResult = ReviewedDocument.CreateSubmitted(
            documentId,
            request.TenantId,
            membership.DepartmentId.Value,
            request.MembershipId,
            originalFileName,
            contentType,
            request.VendorName,
            request.Reference,
            request.DocumentDate,
            request.Category,
            request.VendorTaxId,
            request.Subtotal,
            request.DocumentDiscountPercent,
            request.DocumentDiscountAmount,
            request.Vat,
            request.TotalAmount,
            string.IsNullOrWhiteSpace(request.Source) ? "manual-submission" : request.Source,
            request.ReviewedByStaff,
            string.IsNullOrWhiteSpace(request.ConfidenceLabel) ? "Staff corrected" : request.ConfidenceLabel,
            request.SubmittedAt,
            lineItems,
            taxLines);

        if (documentResult.IsFailure)
            return Result.Failure<ReviewedDocumentResponse>(documentResult.Error);

        // Carry currency snapshot. Three sources, in priority:
        //  1. Caller-supplied CurrencyCode/ExchangeRate on the command (manual override).
        //  2. Linked draft's snapshot (already resolved at upload time).
        //  3. Tenant base currency at rate 1.0 (legacy / VND-only flows).
        var documentEntity = documentResult.Value;

        string documentCurrency;
        string baseCurrency;
        decimal exchangeRate;

        if (!string.IsNullOrWhiteSpace(request.CurrencyCode) || request.ExchangeRate.HasValue)
        {
            // Caller provided override — fetch base currency separately, resolve rate if needed.
            var ctx = await ResolveCurrencyContextAsync(
                request.TenantId,
                request.CurrencyCode,
                request.ExchangeRate,
                request.DocumentDate,
                draft?.BaseCurrencyCode,
                cancellationToken);

            if (ctx.IsFailure)
                return Result.Failure<ReviewedDocumentResponse>(ctx.Error);

            documentCurrency = ctx.Value.DocumentCurrency;
            baseCurrency = ctx.Value.BaseCurrency;
            exchangeRate = ctx.Value.ExchangeRate;
        }
        else if (draft is not null)
        {
            documentCurrency = draft.CurrencyCode;
            baseCurrency = draft.BaseCurrencyCode;
            exchangeRate = draft.ExchangeRate;
        }
        else
        {
            // Manual submission, no caller-provided currency — fallback to tenant base.
            var tenantSummary = await _tenantRepository.GetByIdAsync(request.TenantId, cancellationToken);
            baseCurrency = string.IsNullOrWhiteSpace(tenantSummary?.Currency)
                ? DefaultBaseCurrency
                : tenantSummary!.Currency;
            documentCurrency = baseCurrency;
            exchangeRate = 1m;
        }

        var setCurrency = documentEntity.SetCurrencyContext(documentCurrency, baseCurrency, exchangeRate);
        if (setCurrency.IsFailure)
            return Result.Failure<ReviewedDocumentResponse>(setCurrency.Error);

        // Resolve vendor link. Soft-failure: when the resolver returns
        // NotApplicable (empty/invalid tax code) the document still saves with
        // IdVendor=null. Hard failure (e.g. constraint race that re-fetch
        // couldn't recover) bubbles up.
        var linkResult = await _vendorLinkResolver.ResolveAsync(
            new VendorLinkRequest(
                TenantId: request.TenantId,
                VendorTaxId: request.VendorTaxId,
                VendorName: request.VendorName,
                CreatedByMembershipId: request.MembershipId,
                SourceDocumentId: documentEntity.Id),
            cancellationToken);
        if (linkResult.IsFailure)
            return Result.Failure<ReviewedDocumentResponse>(linkResult.Error);
        documentEntity.LinkVendor(linkResult.Value.VendorId);

        if (linkResult.Value.VendorId.HasValue && _vendorRepository is not null)
        {
            var canonicalVendor = await _vendorRepository.GetByIdAsync(
                linkResult.Value.VendorId.Value,
                request.TenantId,
                cancellationToken);

            if (canonicalVendor is not null)
                documentEntity.RefreshVendorSnapshot(canonicalVendor.Name, canonicalVendor.TaxCode);
        }

        // Duplicate-receipt detection. Soft-warning only — submit proceeds even
        // when matches are found. Notification mapper fans out to accountants.
        var dedupHash = Duplicates.DocumentDedupHasher.Compute(
            request.VendorTaxId,
            request.VendorName,
            request.Reference,
            request.DocumentDate,
            request.TotalAmount);
        documentEntity.SetDedupHash(dedupHash);
        if (dedupHash is not null)
        {
            var matches = await _duplicateReceiptDetector.FindMatchesAsync(
                request.TenantId,
                dedupHash,
                documentEntity.Id,
                DuplicateSlidingWindowDays,
                cancellationToken);
            if (matches.Count > 0)
                documentEntity.FlagAsPotentialDuplicate(matches);
        }

        if (draft != null)
        {
            var markSubmittedResult = draft.MarkSubmitted();
            if (markSubmittedResult.IsFailure)
                return Result.Failure<ReviewedDocumentResponse>(markSubmittedResult.Error);

            _uploadedDocumentDraftRepository.Update(draft);
        }

        _reviewedDocumentRepository.Add(documentEntity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await _documentChunkIndexer.ReindexAsync(documentEntity, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Reviewed document auto-index failed after submit for tenant {TenantId} document {DocumentId}",
                documentEntity.IdTenant,
                documentEntity.Id);
        }

        return Result.Success(new ReviewedDocumentResponse(
            documentEntity.Id,
            documentEntity.Status.ToString(),
            documentEntity.SubmittedAt,
            documentEntity.VendorName,
            documentEntity.Reference,
            documentEntity.TotalAmount,
            documentEntity.ReviewedByStaff,
            documentEntity.CurrencyCode,
            documentEntity.ExchangeRate,
            documentEntity.BaseCurrencyCode,
            documentEntity.TotalAmountInBaseCurrency));
    }

    private async Task<Result<CurrencyContext>> ResolveCurrencyContextAsync(
        Guid tenantId,
        string? requestedCurrency,
        decimal? requestedRate,
        DateOnly documentDate,
        string? existingBaseCurrency,
        CancellationToken cancellationToken)
    {
        var baseCurrency = !string.IsNullOrWhiteSpace(existingBaseCurrency)
            ? existingBaseCurrency!
            : (await _tenantRepository.GetByIdAsync(tenantId, cancellationToken))?.Currency ?? DefaultBaseCurrency;

        var documentCurrency = string.IsNullOrWhiteSpace(requestedCurrency)
            ? baseCurrency
            : requestedCurrency!.Trim().ToUpperInvariant();

        if (string.Equals(documentCurrency, baseCurrency, StringComparison.OrdinalIgnoreCase))
            return Result.Success(new CurrencyContext(baseCurrency, baseCurrency, 1m));

        if (requestedRate is { } explicitRate && explicitRate > 0m)
            return Result.Success(new CurrencyContext(documentCurrency, baseCurrency, explicitRate));

        var rateResult = await _exchangeRateService.GetRateAsync(
            documentCurrency, baseCurrency, documentDate, cancellationToken);

        if (rateResult.IsSuccess)
            return Result.Success(new CurrencyContext(documentCurrency, baseCurrency, rateResult.Value.Rate));

        _logger.LogWarning(
            "Submit document currency rate {From}->{To} on {Date} unavailable; falling back to base currency. {Error}",
            documentCurrency, baseCurrency, documentDate, rateResult.Error.Description);

        return Result.Success(new CurrencyContext(baseCurrency, baseCurrency, 1m));
    }

    private readonly record struct CurrencyContext(string DocumentCurrency, string BaseCurrency, decimal ExchangeRate);
}
