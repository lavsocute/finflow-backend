namespace FinFlow.Application.Documents.DTOs.Responses;

public sealed record DocumentOcrDraftResponse(
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
    string ReviewedByStaff,
    string ConfidenceLabel,
    IReadOnlyList<DocumentOcrDraftLineItemResponse> LineItems
);
