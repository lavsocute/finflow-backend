using FinFlow.Application.Common;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Documents.Commands.SubmitReviewedDocument;

public sealed record SubmitReviewedDocumentLineItem(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total
);

public sealed record SubmitReviewedDocumentCommand(
    Guid? DraftId,
    Guid TenantId,
    Guid MembershipId,
    string OriginalFileName,
    string VendorName,
    string Reference,
    DateOnly DocumentDate,
    DateOnly DueDate,
    string Category,
    string? VendorTaxId,
    decimal Subtotal,
    decimal Vat,
    decimal TotalAmount,
    string Source,
    string ReviewedByStaff,
    string ConfidenceLabel,
    DateTime SubmittedAt,
    IReadOnlyList<SubmitReviewedDocumentLineItem> LineItems
) : ICommand<Result<ReviewedDocumentResponse>>;
