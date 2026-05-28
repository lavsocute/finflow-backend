using FinFlow.Application.Common;
using FinFlow.Application.Documents.Commands;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Documents.Commands.SaveReviewedOcrDraft;

public sealed record SaveReviewedOcrDraftLineItem(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total,
    decimal? DiscountPercent = null,
    decimal DiscountAmount = 0m,
    decimal? TaxRate = null,
    decimal TaxableAmount = 0m,
    decimal TaxAmount = 0m
);

public sealed record SaveReviewedOcrDraftCommand(
    Guid DraftId,
    Guid TenantId,
    Guid MembershipId,
    string VendorName,
    string Reference,
    DateOnly DocumentDate,
    string Category,
    string? VendorTaxId,
    decimal Subtotal,
    decimal Vat,
    decimal TotalAmount,
    string ConfidenceLabel,
    IReadOnlyList<SaveReviewedOcrDraftLineItem> LineItems,
    IReadOnlyList<DocumentTaxLineInput>? TaxLines = null
) : ICommand<Result<Guid>>;
