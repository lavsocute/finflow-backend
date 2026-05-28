namespace FinFlow.Application.Documents.Ocr;

public sealed record OcrExtractionLineItem(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total,
    decimal? TaxRate = null,
    decimal TaxableAmount = 0m,
    decimal TaxAmount = 0m
);
