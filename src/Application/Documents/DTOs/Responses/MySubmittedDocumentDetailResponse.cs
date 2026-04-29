namespace FinFlow.Application.Documents.DTOs.Responses;

public sealed record MySubmittedDocumentDetailLineItemResponse(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total);

public sealed record MySubmittedDocumentDetailResponse(
    Guid DocumentId,
    string OriginalFileName,
    string ContentType,
    string VendorName,
    string Reference,
    DateOnly DocumentDate,
    DateOnly DueDate,
    string Category,
    string VendorTaxId,
    decimal Subtotal,
    decimal Vat,
    decimal TotalAmount,
    string Source,
    string Status,
    string SubmittedByEmail,
    DateTime SubmittedAt,
    DateTime LastUpdatedAt,
    string? RejectionReason,
    IReadOnlyList<MySubmittedDocumentDetailLineItemResponse> LineItems);