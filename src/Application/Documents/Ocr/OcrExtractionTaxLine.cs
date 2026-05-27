namespace FinFlow.Application.Documents.Ocr;

public sealed record OcrExtractionTaxLine(
    string TaxType,
    decimal? Rate,
    decimal TaxableAmount,
    decimal TaxAmount
);
