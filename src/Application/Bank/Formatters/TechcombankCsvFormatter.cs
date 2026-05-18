using System.Globalization;

namespace FinFlow.Application.Bank.Formatters;

/// <summary>
/// Techcombank corporate bulk transfer template (verified 2026-05).
/// Semicolon-separated, English headers (TCB Business platform).
/// </summary>
internal sealed class TechcombankCsvFormatter : IBankCsvFormatter
{
    public string FormatCode => "TCB";
    public string FormatName => "Techcombank Business (CSV)";
    public string FileExtension => ".csv";
    public string Separator => ";";

    private static readonly string[] Headers =
    [
        "No", "BeneficiaryName", "BeneficiaryAccount",
        "BeneficiaryBank", "Branch", "Amount",
        "Currency", "Description"
    ];

    public string Format(IReadOnlyList<PaymentExportRow> rows)
    {
        var data = rows.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Sequence.ToString(CultureInfo.InvariantCulture),
            r.PayeeName,
            r.PayeeBankAccountNumber,
            r.PayeeBankCode,
            r.PayeeBankBranch ?? string.Empty,
            r.Amount.ToString("0.##", CultureInfo.InvariantCulture),
            r.CurrencyCode,
            r.TransferNote
        }).ToList();

        return CsvWriter.Build(Separator, Headers, data);
    }
}
