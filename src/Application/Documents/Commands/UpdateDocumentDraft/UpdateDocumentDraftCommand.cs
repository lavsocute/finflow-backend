using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Application.Documents.Commands;
using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.Documents.Commands.UpdateDocumentDraft;

public sealed record UpdateDocumentDraftCommand(
    Guid DraftId,
    Guid TenantId,
    Guid MembershipId,
    bool IsTenantOwner,
    string VendorName,
    string Reference,
    DateOnly DocumentDate,
    string Category,
    string? VendorTaxId,
    decimal Subtotal,
    decimal? DocumentDiscountPercent,
    decimal DocumentDiscountAmount,
    decimal Vat,
    decimal TotalAmount,
    string ConfidenceLabel,
    IReadOnlyList<UpdateDocumentDraftLineItem> LineItems,
    string? CurrencyCode = null,
    decimal? ExchangeRate = null,
    IReadOnlyList<DocumentTaxLineInput>? TaxLines = null
) : IRequest<Result<DocumentOcrDraftResponse>>;

public sealed record UpdateDocumentDraftLineItem(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal? DiscountPercent,
    decimal DiscountAmount,
    decimal Total);
