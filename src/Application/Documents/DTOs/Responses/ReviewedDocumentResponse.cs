namespace FinFlow.Application.Documents.DTOs.Responses;

public sealed record ReviewedDocumentResponse(
    Guid DocumentId,
    string Status,
    DateTime SubmittedAt,
    string VendorName,
    string Reference,
    decimal TotalAmount,
    string ReviewedByStaff,
    string CurrencyCode = "VND",
    decimal ExchangeRate = 1m,
    string BaseCurrencyCode = "VND",
    decimal TotalAmountInBaseCurrency = 0m
);
