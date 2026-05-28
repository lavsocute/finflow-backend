namespace FinFlow.Application.Documents.DTOs.Responses;

public sealed record DocumentOcrDraftLineItemResponse(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total,
    decimal? DiscountPercent = null,
    decimal DiscountAmount = 0m,
    decimal? TaxRate = null,
    decimal TaxableAmount = 0m,
    decimal TaxAmount = 0m
);
