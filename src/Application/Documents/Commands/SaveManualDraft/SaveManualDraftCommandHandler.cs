using FinFlow.Application.Common.ExchangeRates;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Tenants;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FinFlow.Application.Documents.Commands.SaveManualDraft;

public sealed class SaveManualDraftCommandHandler
    : IRequestHandler<SaveManualDraftCommand, Result<Guid>>
{
    private const string DefaultBaseCurrency = "VND";

    private readonly IUploadedDocumentDraftRepository _uploadedDocumentDraftRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantRepository _tenantRepository;
    private readonly IExchangeRateService _exchangeRateService;
    private readonly ILogger<SaveManualDraftCommandHandler> _logger;

    public SaveManualDraftCommandHandler(
        IUploadedDocumentDraftRepository uploadedDocumentDraftRepository,
        IUnitOfWork unitOfWork,
        ITenantRepository tenantRepository,
        IExchangeRateService exchangeRateService,
        ILogger<SaveManualDraftCommandHandler> logger)
    {
        _uploadedDocumentDraftRepository = uploadedDocumentDraftRepository;
        _unitOfWork = unitOfWork;
        _tenantRepository = tenantRepository;
        _exchangeRateService = exchangeRateService;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(SaveManualDraftCommand request, CancellationToken cancellationToken)
    {
        var lineItemsResult = request.LineItems
            .Select(item => UploadedDocumentDraftLineItem.Create(item.ItemName, item.Quantity, item.UnitPrice, item.Total))
            .ToList();

        var firstFailure = lineItemsResult.FirstOrDefault(r => r.IsFailure);
        if (firstFailure is not null)
            return Result.Failure<Guid>(firstFailure.Error);

        var lineItems = lineItemsResult.Select(r => r.Value).ToList();
        var taxLinesResult = (request.TaxLines ?? [])
            .Select(item => UploadedDocumentDraftTaxLine.Create(item.TaxType, item.Rate, item.TaxableAmount, item.TaxAmount))
            .ToList();
        var firstTaxFailure = taxLinesResult.FirstOrDefault(r => r.IsFailure);
        if (firstTaxFailure is not null)
            return Result.Failure<Guid>(firstTaxFailure.Error);
        var taxLines = taxLinesResult.Select(r => r.Value).ToList();

        var draftResult = UploadedDocumentDraft.CreateManual(
            request.TenantId,
            request.MembershipId,
            request.OriginalFileName,
            request.VendorName,
            request.Reference,
            request.DocumentDate,
            request.Category,
            request.VendorTaxId,
            request.Subtotal,
            request.Vat,
            request.TotalAmount,
            request.ReviewedByStaff,
            lineItems,
            taxLines);

        if (draftResult.IsFailure)
            return Result.Failure<Guid>(draftResult.Error);

        var draft = draftResult.Value;

        // Resolve currency context. Manual entry path:
        //  - User may explicitly send CurrencyCode (e.g. invoice in USD).
        //  - User may send ExchangeRate to override the auto-fetched rate
        //    (e.g. internal accounting rate that differs from market).
        //  - When both omitted, fall back to tenant base currency at rate 1.0.
        var currencyResult = await ResolveCurrencyContextAsync(
            request.TenantId,
            request.CurrencyCode,
            request.ExchangeRate,
            request.DocumentDate,
            cancellationToken);
        if (currencyResult.IsFailure)
            return Result.Failure<Guid>(currencyResult.Error);

        var ctx = currencyResult.Value;
        var setCurrency = draft.SetCurrencyContext(ctx.DocumentCurrency, ctx.BaseCurrency, ctx.ExchangeRate);
        if (setCurrency.IsFailure)
            return Result.Failure<Guid>(setCurrency.Error);

        _uploadedDocumentDraftRepository.Add(draft);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(draft.Id);
    }

    private async Task<Result<CurrencyContext>> ResolveCurrencyContextAsync(
        Guid tenantId,
        string? requestedCurrency,
        decimal? requestedRate,
        DateOnly documentDate,
        CancellationToken cancellationToken)
    {
        var tenantSummary = await _tenantRepository.GetByIdAsync(tenantId, cancellationToken);
        var baseCurrency = string.IsNullOrWhiteSpace(tenantSummary?.Currency)
            ? DefaultBaseCurrency
            : tenantSummary!.Currency;

        var documentCurrency = string.IsNullOrWhiteSpace(requestedCurrency)
            ? baseCurrency
            : requestedCurrency!.Trim().ToUpperInvariant();

        // Same currency → unit rate, ignore any caller-supplied override.
        if (string.Equals(documentCurrency, baseCurrency, StringComparison.OrdinalIgnoreCase))
            return Result.Success(new CurrencyContext(baseCurrency, baseCurrency, 1m));

        // Caller supplied an override — trust it (subject to entity validation).
        if (requestedRate is { } explicitRate && explicitRate > 0m)
            return Result.Success(new CurrencyContext(documentCurrency, baseCurrency, explicitRate));

        // Auto-fetch rate via service.
        var rateResult = await _exchangeRateService.GetRateAsync(
            documentCurrency, baseCurrency, documentDate, cancellationToken);

        if (rateResult.IsSuccess)
            return Result.Success(new CurrencyContext(documentCurrency, baseCurrency, rateResult.Value.Rate));

        _logger.LogWarning(
            "Manual draft currency rate {From}->{To} on {Date} unavailable; falling back to base currency. {Error}",
            documentCurrency, baseCurrency, documentDate, rateResult.Error.Description);

        // Fail-safe: store as base currency at rate 1.0 so the draft is usable.
        return Result.Success(new CurrencyContext(baseCurrency, baseCurrency, 1m));
    }

    private readonly record struct CurrencyContext(string DocumentCurrency, string BaseCurrency, decimal ExchangeRate);
}
