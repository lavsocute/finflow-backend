namespace FinFlow.Application.Documents.Ocr;

public sealed record OcrExtractionLineItem(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total
);
