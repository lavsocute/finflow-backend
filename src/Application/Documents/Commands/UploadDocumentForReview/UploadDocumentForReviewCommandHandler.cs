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
    private readonly ISubscriptionFeatureGate _subscriptionFeatureGate;
    private readonly ITenantUsageService _tenantUsageService;
    private readonly ITenantSubscriptionRepository _tenantSubscriptionRepository;
    private readonly IDocumentStorageProvider _documentStorageProvider;

public UploadDocumentForReviewCommandHandler(
        IUploadedDocumentDraftRepository uploadedDocumentDraftRepository,
        IUnitOfWork unitOfWork,
        IOcrExtractionService ocrExtractionService)
        : this(
            uploadedDocumentDraftRepository,
            unitOfWork,
            ocrExtractionService,
            new AllowAllSubscriptionFeatureGate(),
            new NoOpTenantUsageService(),
            new NullTenantSubscriptionRepository(),
            new NoOpDocumentStorageProvider())
    {
    }

    public UploadDocumentForReviewCommandHandler(
        IUploadedDocumentDraftRepository uploadedDocumentDraftRepository,
        IUnitOfWork unitOfWork,
        IOcrExtractionService ocrExtractionService,
        ISubscriptionFeatureGate subscriptionFeatureGate)
        : this(
            uploadedDocumentDraftRepository,
            unitOfWork,
            ocrExtractionService,
            subscriptionFeatureGate,
            new NoOpTenantUsageService(),
            new NullTenantSubscriptionRepository(),
            new NoOpDocumentStorageProvider())
    {
    }

    public UploadDocumentForReviewCommandHandler(
        IUploadedDocumentDraftRepository uploadedDocumentDraftRepository,
        IUnitOfWork unitOfWork,
        IOcrExtractionService ocrExtractionService,
        ISubscriptionFeatureGate subscriptionFeatureGate,
        ITenantUsageService tenantUsageService,
        ITenantSubscriptionRepository tenantSubscriptionRepository,
        IDocumentStorageProvider documentStorageProvider)
    {
        _uploadedDocumentDraftRepository = uploadedDocumentDraftRepository;
        _unitOfWork = unitOfWork;
        _ocrExtractionService = ocrExtractionService;
        _subscriptionFeatureGate = subscriptionFeatureGate;
        _tenantUsageService = tenantUsageService;
        _tenantSubscriptionRepository = tenantSubscriptionRepository;
        _documentStorageProvider = documentStorageProvider;
    }

    public async Task<Result<DocumentOcrDraftResponse>> Handle(UploadDocumentForReviewCommand request, CancellationToken cancellationToken)
    {
        var fileName = request.FileName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
            return Result.Failure<DocumentOcrDraftResponse>(UploadedDocumentDraftErrors.FileNameRequired);

var contentType = request.ContentType?.Trim() ?? string.Empty;
        if (!SupportedContentTypes.Contains(contentType))
            return Result.Failure<DocumentOcrDraftResponse>(UploadedDocumentDraftErrors.UnsupportedContentType);

        if (request.FileContents.Length > MaxFileSizeBytes)
            return Result.Failure<DocumentOcrDraftResponse>(UploadedDocumentDraftErrors.FileTooLarge);

        var gateResult = await _subscriptionFeatureGate.EnsureFeatureEnabledAsync(
            request.TenantId,
            SubscriptionFeature.DocumentsOcr,
            cancellationToken);

        if (gateResult.IsFailure)
            return Result.Failure<DocumentOcrDraftResponse>(gateResult.Error);

        var pageCountResult = await _ocrExtractionService.GetPageCountAsync(
            contentType,
            request.FileContents,
            cancellationToken);
        if (pageCountResult.IsFailure)
            return Result.Failure<DocumentOcrDraftResponse>(pageCountResult.Error);

        var processedPageCount = pageCountResult.Value;

        var quotaResult = await _subscriptionFeatureGate.EnsureOcrAllowedAsync(
            request.TenantId,
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
            extracted.DueDate,
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

var subscription = await _tenantSubscriptionRepository.GetByTenantIdAsync(request.TenantId, cancellationToken);

        if (subscription is null)
            throw new InvalidOperationException(TenantSubscriptionErrors.SubscriptionNotFound.Description);

        var usagePeriodStart = DateOnly.FromDateTime(subscription.PeriodStart);
        var usagePeriodEnd = DateOnly.FromDateTime(subscription.PeriodEnd);

        await _tenantUsageService.RecordOcrUsageAsync(
            request.TenantId,
            processedPageCount,
            usagePeriodStart,
            usagePeriodEnd,
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var draft = draftResult.Value;
        var response = new DocumentOcrDraftResponse(
            draft.Id,
            draft.OriginalFileName,
            draft.ContentType,
            draft.VendorName,
            draft.Reference,
            draft.DocumentDate,
            draft.DueDate,
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

    private sealed class AllowAllSubscriptionFeatureGate : ISubscriptionFeatureGate
    {
        public Task<PlanEntitlements> GetEntitlementsAsync(Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(new PlanEntitlements(true, true, true, long.MaxValue, int.MaxValue, int.MaxValue));

        public Task<Result> EnsureFeatureEnabledAsync(Guid tenantId, SubscriptionFeature feature, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());

        public Task<Result> EnsureOcrAllowedAsync(Guid tenantId, int pageCount, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());

        public Task<Result> EnsureChatbotAllowedAsync(Guid tenantId, int messageCount, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private sealed class NoOpTenantUsageService : ITenantUsageService
    {
        public Task<TenantUsageSnapshot> GetCurrentUsageAsync(
            Guid tenantId,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
        {
            var snapshotResult = TenantUsageSnapshot.Create(tenantId, periodStart, periodEnd);
            if (snapshotResult.IsFailure)
                throw new InvalidOperationException(snapshotResult.Error.Description);

            return Task.FromResult(snapshotResult.Value);
        }

        public Task RecordOcrUsageAsync(
            Guid tenantId,
            int pageCount,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordChatbotUsageAsync(
            Guid tenantId,
            int messageCount,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetStorageUsedBytesAsync(
            Guid tenantId,
            long storageUsedBytes,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NullTenantSubscriptionRepository : ITenantSubscriptionRepository
    {
        public Task<TenantSubscription?> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default)
            => Task.FromResult<TenantSubscription?>(null);

        public void Add(TenantSubscription subscription)
        {
        }

public void Update(TenantSubscription subscription)
        {
        }
    }

    private sealed class NoOpDocumentStorageProvider : IDocumentStorageProvider
    {
        public Task SaveImageAsync(Guid documentId, byte[] imageData, string contentType, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<byte[]?> GetImageAsync(Guid documentId, CancellationToken cancellationToken = default)
            => Task.FromResult<byte[]?>(null);

        public Task DeleteImageAsync(Guid documentId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string?> GetContentTypeAsync(Guid documentId, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
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

        var roundedTotalAmount = decimal.Round(extracted.TotalAmount, 2, MidpointRounding.AwayFromZero);
        var roundedLineItemTotal = decimal.Round(extracted.LineItems.Sum(item => item.Total), 2, MidpointRounding.AwayFromZero);
        if (roundedLineItemTotal != roundedTotalAmount)
            return Result.Failure(UploadedDocumentDraftErrors.LineItemTotalsMismatch);

        var roundedBreakdownTotal = decimal.Round(extracted.Subtotal + extracted.Vat, 2, MidpointRounding.AwayFromZero);
        if (roundedBreakdownTotal != roundedTotalAmount)
            return Result.Failure(UploadedDocumentDraftErrors.FinancialBreakdownMismatch);

        return Result.Success();
    }
}
