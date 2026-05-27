using FinFlow.Application.Common.ExchangeRates;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Tenants;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FinFlow.Application.Documents.Commands.UpdateDocumentDraft;

internal sealed class UpdateDocumentDraftCommandHandler
    : IRequestHandler<UpdateDocumentDraftCommand, Result<DocumentOcrDraftResponse>>
{
    private const string DefaultBaseCurrency = "VND";

    private readonly IUploadedDocumentDraftRepository _draftRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantRepository _tenantRepository;
    private readonly IExchangeRateService _exchangeRateService;
    private readonly ILogger<UpdateDocumentDraftCommandHandler> _logger;

    public UpdateDocumentDraftCommandHandler(
        IUploadedDocumentDraftRepository draftRepo,
        IUnitOfWork unitOfWork,
        ITenantRepository tenantRepository,
        IExchangeRateService exchangeRateService,
        ILogger<UpdateDocumentDraftCommandHandler> logger)
    {
        _draftRepo = draftRepo;
        _unitOfWork = unitOfWork;
        _tenantRepository = tenantRepository;
        _exchangeRateService = exchangeRateService;
        _logger = logger;
    }

    public async Task<Result<DocumentOcrDraftResponse>> Handle(UpdateDocumentDraftCommand cmd, CancellationToken ct)
    {
        var draft = cmd.IsTenantOwner
            ? await _draftRepo.GetByTenantIdAsync(cmd.DraftId, cmd.TenantId, ct)
            : await _draftRepo.GetByIdAsync(cmd.DraftId, cmd.TenantId, cmd.MembershipId, ct);

        if (draft is null)
            return Result.Failure<DocumentOcrDraftResponse>(UploadedDocumentDraftErrors.NotFound);

        // Build line items via factory (validates discount invariants per line)
        var lineItems = new List<UploadedDocumentDraftLineItem>();
        foreach (var li in cmd.LineItems)
        {
            var r = UploadedDocumentDraftLineItem.Create(
                li.ItemName, li.Quantity, li.UnitPrice, li.DiscountPercent, li.DiscountAmount, li.Total);
            if (r.IsFailure)
                return Result.Failure<DocumentOcrDraftResponse>(r.Error);
            lineItems.Add(r.Value);
        }

        var taxLines = new List<UploadedDocumentDraftTaxLine>();
        foreach (var taxLine in cmd.TaxLines ?? [])
        {
            var r = UploadedDocumentDraftTaxLine.Create(
                taxLine.TaxType,
                taxLine.Rate,
                taxLine.TaxableAmount,
                taxLine.TaxAmount);
            if (r.IsFailure)
                return Result.Failure<DocumentOcrDraftResponse>(r.Error);
            taxLines.Add(r.Value);
        }

        var updateResult = draft.UpdateDraftFields(
            cmd.VendorName,
            cmd.Reference,
            cmd.DocumentDate,
            cmd.Category,
            cmd.VendorTaxId,
            cmd.Subtotal,
            cmd.DocumentDiscountPercent,
            cmd.DocumentDiscountAmount,
            cmd.Vat,
            cmd.TotalAmount,
            cmd.ConfidenceLabel,
            lineItems,
            taxLines);

        if (updateResult.IsFailure)
            return Result.Failure<DocumentOcrDraftResponse>(updateResult.Error);

        // Currency edit path. Reviewer may correct a wrong OCR currency
        // (e.g. inferred USD but invoice is actually AUD), or override the rate.
        // We re-resolve only when the caller provided either field; otherwise the
        // existing snapshot stays intact.
        if (cmd.CurrencyCode is not null || cmd.ExchangeRate is not null)
        {
            var ctxResult = await ResolveCurrencyContextAsync(
                cmd.TenantId,
                cmd.CurrencyCode ?? draft.CurrencyCode,
                cmd.ExchangeRate,
                cmd.DocumentDate,
                draft.BaseCurrencyCode,
                ct);

            if (ctxResult.IsFailure)
                return Result.Failure<DocumentOcrDraftResponse>(ctxResult.Error);

            var ctx = ctxResult.Value;
            var setCurrency = draft.SetCurrencyContext(ctx.DocumentCurrency, ctx.BaseCurrency, ctx.ExchangeRate);
            if (setCurrency.IsFailure)
                return Result.Failure<DocumentOcrDraftResponse>(setCurrency.Error);
        }

        _draftRepo.Update(draft);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Document draft updated: {DraftId} tenant={TenantId} membership={MembershipId} byTenantOwner={IsTenantOwner}",
            draft.Id, cmd.TenantId, cmd.MembershipId, cmd.IsTenantOwner);

        return Result.Success(MapToResponse(draft));
    }

    private async Task<Result<CurrencyContext>> ResolveCurrencyContextAsync(
        Guid tenantId,
        string requestedCurrency,
        decimal? requestedRate,
        DateOnly documentDate,
        string existingBaseCurrency,
        CancellationToken cancellationToken)
    {
        // Base currency is locked at draft creation — never changes during edit.
        var baseCurrency = string.IsNullOrWhiteSpace(existingBaseCurrency)
            ? DefaultBaseCurrency
            : existingBaseCurrency;

        // Fall back to tenant default when caller passes blank.
        if (string.IsNullOrWhiteSpace(baseCurrency))
        {
            var tenantSummary = await _tenantRepository.GetByIdAsync(tenantId, cancellationToken);
            baseCurrency = string.IsNullOrWhiteSpace(tenantSummary?.Currency)
                ? DefaultBaseCurrency
                : tenantSummary!.Currency;
        }

        var documentCurrency = string.IsNullOrWhiteSpace(requestedCurrency)
            ? baseCurrency
            : requestedCurrency.Trim().ToUpperInvariant();

        if (string.Equals(documentCurrency, baseCurrency, StringComparison.OrdinalIgnoreCase))
            return Result.Success(new CurrencyContext(baseCurrency, baseCurrency, 1m));

        if (requestedRate is { } explicitRate && explicitRate > 0m)
            return Result.Success(new CurrencyContext(documentCurrency, baseCurrency, explicitRate));

        var rateResult = await _exchangeRateService.GetRateAsync(
            documentCurrency, baseCurrency, documentDate, cancellationToken);

        if (rateResult.IsSuccess)
            return Result.Success(new CurrencyContext(documentCurrency, baseCurrency, rateResult.Value.Rate));

        _logger.LogWarning(
            "Update draft currency rate {From}->{To} on {Date} unavailable; keeping base currency. {Error}",
            documentCurrency, baseCurrency, documentDate, rateResult.Error.Description);

        return Result.Success(new CurrencyContext(baseCurrency, baseCurrency, 1m));
    }

    private static DocumentOcrDraftResponse MapToResponse(UploadedDocumentDraft draft) =>
        new(
            draft.Id,
            draft.OriginalFileName,
            draft.ContentType,
            draft.HasImage,
            BuildPreviewImageDataUrl(draft.ImageContentType, draft.ImageData),
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
            draft.LineItems
                .Select(li => new DocumentOcrDraftLineItemResponse(li.ItemName, li.Quantity, li.UnitPrice, li.Total))
                .ToList(),
            draft.TaxLines
                .Select(taxLine => new DocumentTaxLineResponse(taxLine.TaxType, taxLine.Rate, taxLine.TaxableAmount, taxLine.TaxAmount))
                .ToList(),
            draft.CurrencyCode,
            draft.ExchangeRate,
            draft.BaseCurrencyCode,
            decimal.Round(draft.TotalAmount * draft.ExchangeRate, 2, MidpointRounding.AwayFromZero));

    private static string? BuildPreviewImageDataUrl(string? contentType, byte[]? imageData)
    {
        if (imageData is not { Length: > 0 } || string.IsNullOrWhiteSpace(contentType))
            return null;

        return $"data:{contentType};base64,{Convert.ToBase64String(imageData)}";
    }

    private readonly record struct CurrencyContext(string DocumentCurrency, string BaseCurrency, decimal ExchangeRate);
}
