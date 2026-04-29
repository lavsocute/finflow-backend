using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Documents.Commands.SaveReviewedOcrDraft;

public sealed record SaveReviewedOcrDraftLineItem(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total
);

public sealed record SaveReviewedOcrDraftCommand(
    Guid DraftId,
    Guid TenantId,
    Guid MembershipId,
    string VendorName,
    string Reference,
    DateOnly DocumentDate,
    DateOnly DueDate,
    string Category,
    string? VendorTaxId,
    decimal Subtotal,
    decimal Vat,
    decimal TotalAmount,
    string ConfidenceLabel,
    IReadOnlyList<SaveReviewedOcrDraftLineItem> LineItems
) : ICommand<Result<Guid>>;