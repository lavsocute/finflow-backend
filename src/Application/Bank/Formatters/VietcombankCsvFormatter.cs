using System.Globalization;

namespace FinFlow.Application.Bank.Formatters;

/// <summary>
/// Vietcombank corporate ACH bulk transfer template (verified 2026-05).
/// Comma-separated, UTF-8 with BOM, headers in Vietnamese without diacritics
/// (matches what VCB Cash Manager actually accepts).
/// </summary>
internal sealed class VietcombankCsvFormatter : IBankCsvFormatter
{
    public string FormatCode => "VCB";
    public string FormatName => "Vietcombank ACH (CSV)";
    public string FileExtension => ".csv";
    public string Separator => ",";

    private static readonly string[] Headers =
    [
        "STT", "Ten nguoi nhan", "So tai khoan", "Ngan hang nhan",
        "Chi nhanh", "So tien", "Loai tien", "Noi dung CK"
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
