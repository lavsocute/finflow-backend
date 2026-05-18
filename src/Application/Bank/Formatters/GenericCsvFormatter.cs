using System.Globalization;

namespace FinFlow.Application.Bank.Formatters;

/// <summary>
/// Universal CSV format — comma-separated, English headers, no bank-specific quirks.
/// Use when accountant's bank doesn't have a dedicated adapter (vd: ACB, MBB, OCB...)
/// — they hand-massage the file before uploading.
/// </summary>
internal sealed class GenericCsvFormatter : IBankCsvFormatter
{
    public string FormatCode => "GENERIC";
    public string FormatName => "Generic CSV";
    public string FileExtension => ".csv";
    public string Separator => ",";

    private static readonly string[] Headers =
    [
        "Sequence", "PayeeName", "BankCode", "AccountNumber",
        "Branch", "Amount", "Currency", "TransferNote", "Reference"
    ];

    public string Format(IReadOnlyList<PaymentExportRow> rows)
    {
        var data = rows.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Sequence.ToString(CultureInfo.InvariantCulture),
            r.PayeeName,
            r.PayeeBankCode,
            r.PayeeBankAccountNumber,
            r.PayeeBankBranch ?? string.Empty,
            r.Amount.ToString("0.##", CultureInfo.InvariantCulture),
            r.CurrencyCode,
            r.TransferNote,
            r.ExportReference
        }).ToList();

        return CsvWriter.Build(Separator, Headers, data);
    }
}
