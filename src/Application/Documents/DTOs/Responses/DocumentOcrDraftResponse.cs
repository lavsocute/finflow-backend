namespace FinFlow.Application.Documents.DTOs.Responses;

public sealed record DocumentOcrDraftResponse(
    Guid DocumentId,
    string OriginalFileName,
    string ContentType,
    bool HasPreviewImage,
    string? PreviewImageDataUrl,
    string VendorName,
    string Reference,
    DateOnly DocumentDate,
    string Category,
    string VendorTaxId,
    decimal Subtotal,
    decimal Vat,
    decimal TotalAmount,
    string Source,
    string ReviewedByStaff,
    string ConfidenceLabel,
    IReadOnlyList<DocumentOcrDraftLineItemResponse> LineItems,
    IReadOnlyList<DocumentTaxLineResponse> TaxLines,
    string CurrencyCode = "VND",
    decimal ExchangeRate = 1m,
    string BaseCurrencyCode = "VND",
    decimal TotalAmountInBaseCurrency = 0m,
    int? ProcessedPageCount = null
);
