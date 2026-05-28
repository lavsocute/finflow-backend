using FinFlow.Application.Common;
using FinFlow.Application.Documents.Commands;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Documents.Commands.SaveManualDraft;

public sealed record SaveManualDraftLineItem(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total,
    decimal? TaxRate = null,
    decimal TaxableAmount = 0m,
    decimal TaxAmount = 0m
);

public sealed record SaveManualDraftCommand(
    Guid TenantId,
    Guid MembershipId,
    string OriginalFileName,
    string VendorName,
    string Reference,
    DateOnly DocumentDate,
    string Category,
    string? VendorTaxId,
    decimal Subtotal,
    decimal Vat,
    decimal TotalAmount,
    string ReviewedByStaff,
    IReadOnlyList<SaveManualDraftLineItem> LineItems,
    string? CurrencyCode = null,
    decimal? ExchangeRate = null,
    IReadOnlyList<DocumentTaxLineInput>? TaxLines = null
) : ICommand<Result<Guid>>;
