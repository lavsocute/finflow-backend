namespace FinFlow.Application.Documents.Commands;

public sealed record DocumentTaxLineInput(
    string TaxType,
    decimal? Rate,
    decimal TaxableAmount,
    decimal TaxAmount);
