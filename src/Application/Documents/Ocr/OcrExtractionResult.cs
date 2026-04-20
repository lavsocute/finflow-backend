namespace FinFlow.Application.Documents.Ocr;

public sealed record OcrExtractionResult(
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
    string ConfidenceLabel,
    IReadOnlyList<OcrExtractionLineItem> LineItems
);
