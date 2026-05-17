namespace FinFlow.Application.Documents.DTOs.Responses;

public sealed record DocumentOcrDraftResponse(
    Guid DocumentId,
    string OriginalFileName,
    string ContentType,
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
    bool HasImage,
    IReadOnlyList<DocumentOcrDraftLineItemResponse> LineItems,
    string CurrencyCode = "VND",
    decimal ExchangeRate = 1m,
    string BaseCurrencyCode = "VND",
    decimal TotalAmountInBaseCurrency = 0m
);
