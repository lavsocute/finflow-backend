namespace FinFlow.Application.Documents.DTOs.Responses;

public sealed record MySubmittedDocumentDetailLineItemResponse(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal? DiscountPercent = null,
    decimal DiscountAmount = 0m,
    decimal? TaxRate = null,
    decimal TaxableAmount = 0m,
    decimal TaxAmount = 0m,
    decimal Total = 0m);

public sealed record MySubmittedDocumentDetailResponse(
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
    string CurrencyCode,
    decimal ExchangeRate,
    string BaseCurrencyCode,
    decimal TotalAmountInBaseCurrency,
    string Source,
    string Status,
    string SubmittedByEmail,
    DateTime SubmittedAt,
    DateTime LastUpdatedAt,
    string? RejectionReason,
    IReadOnlyList<MySubmittedDocumentDetailLineItemResponse> LineItems,
    IReadOnlyList<DocumentTaxLineResponse> TaxLines);
