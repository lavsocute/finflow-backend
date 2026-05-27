using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public sealed class UploadedDocumentDraftTaxLine
{
    private UploadedDocumentDraftTaxLine(
        Guid id,
        string taxType,
        decimal? rate,
        decimal taxableAmount,
        decimal taxAmount)
    {
        Id = id;
        TaxType = taxType;
        Rate = rate;
        TaxableAmount = taxableAmount;
        TaxAmount = taxAmount;
    }

    private UploadedDocumentDraftTaxLine() { }

    public Guid Id { get; private set; }
    public string TaxType { get; private set; } = null!;
    public decimal? Rate { get; private set; }
    public decimal TaxableAmount { get; private set; }
    public decimal TaxAmount { get; private set; }

    public static Result<UploadedDocumentDraftTaxLine> Create(
        string taxType,
        decimal? rate,
        decimal taxableAmount,
        decimal taxAmount)
    {
        if (rate is < 0 or > 100)
            return Result.Failure<UploadedDocumentDraftTaxLine>(UploadedDocumentDraftErrors.FinancialBreakdownMismatch);

        if (taxableAmount < 0 || taxAmount < 0)
            return Result.Failure<UploadedDocumentDraftTaxLine>(UploadedDocumentDraftErrors.FinancialBreakdownMismatch);

        return Result.Success(new UploadedDocumentDraftTaxLine(
            Guid.NewGuid(),
            NormalizeTaxType(taxType),
            rate,
            taxableAmount,
            taxAmount));
    }

    private static string NormalizeTaxType(string taxType)
    {
        if (string.IsNullOrWhiteSpace(taxType))
            return "VAT";

        var normalized = taxType.Trim().ToUpperInvariant();
        return normalized.Length <= 32 ? normalized : normalized[..32];
    }
}
