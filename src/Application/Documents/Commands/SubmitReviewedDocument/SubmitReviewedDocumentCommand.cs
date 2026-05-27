using FinFlow.Application.Common;
using FinFlow.Application.Documents.Commands;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Documents.Commands.SubmitReviewedDocument;

public sealed record SubmitReviewedDocumentLineItem(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total,
    decimal? DiscountPercent = null,
    decimal DiscountAmount = 0m
);

public sealed record SubmitReviewedDocumentCommand(
    Guid? DraftId,
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
    string Source,
    string ReviewedByStaff,
    string ConfidenceLabel,
    DateTime SubmittedAt,
    IReadOnlyList<SubmitReviewedDocumentLineItem> LineItems,
    decimal? DocumentDiscountPercent = null,
    decimal DocumentDiscountAmount = 0m,
    string? CurrencyCode = null,
    decimal? ExchangeRate = null,
    IReadOnlyList<DocumentTaxLineInput>? TaxLines = null
) : ICommand<Result<ReviewedDocumentResponse>>;
