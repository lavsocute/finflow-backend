using System.Globalization;

namespace FinFlow.Application.Bank.Formatters;

/// <summary>
/// BIDV corporate banking bulk transfer template (verified 2026-05).
/// Semicolon-separated (BIDV iBank requirement).
/// </summary>
internal sealed class BidvBulkTransferCsvFormatter : IBankCsvFormatter
{
    public string FormatCode => "BIDV";
    public string FormatName => "BIDV Bulk Transfer (CSV)";
    public string FileExtension => ".csv";
    public string Separator => ";";

    private static readonly string[] Headers =
    [
        "Stt", "Ho ten nguoi huong", "So tai khoan nguoi huong",
        "Ngan hang nguoi huong", "Tinh thanh", "So tien",
        "Loai tien", "Noi dung"
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
