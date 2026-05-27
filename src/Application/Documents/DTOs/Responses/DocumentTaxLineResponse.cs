namespace FinFlow.Application.Documents.DTOs.Responses;

public sealed record DocumentTaxLineResponse(
    string TaxType,
    decimal? Rate,
    decimal TaxableAmount,
    decimal TaxAmount);
