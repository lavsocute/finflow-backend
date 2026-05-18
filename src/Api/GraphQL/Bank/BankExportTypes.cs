using FinFlow.Application.Bank;
using FinFlow.Application.Bank.Commands.ExportPaymentsToBankCsv;

namespace FinFlow.Api.GraphQL.Bank;

/// <summary>
/// Catalog entry showing one bank-format option an accountant can pick when
/// exporting payments. Used by the dropdown in the payments page.
/// </summary>
public sealed class BankCsvFormatPayload
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string FileExtension { get; init; } = string.Empty;
    public string Separator { get; init; } = string.Empty;

    public static BankCsvFormatPayload From(IBankCsvFormatter formatter) =>
        new()
        {
            Code = formatter.FormatCode,
            Name = formatter.FormatName,
            FileExtension = formatter.FileExtension,
            Separator = formatter.Separator
        };
}

/// <summary>
/// Input for the <c>exportPaymentsToBankCsv</c> mutation. Caller provides the
/// payment IDs they have selected and the bank format code (e.g. "VCB").
/// </summary>
public sealed class ExportPaymentsToBankCsvInput
{
    public IReadOnlyList<Guid> PaymentIds { get; init; } = [];
    public string BankFormat { get; init; } = string.Empty;
}

/// <summary>
/// Result returned to the accountant. The file body is base64-encoded so it can
/// flow through GraphQL — frontend decodes and offers a download.
/// </summary>
public sealed class ExportPaymentsToBankCsvPayload
{
    public string FileName { get; init; } = string.Empty;
    public string FileBase64 { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public int RowCount { get; init; }
    public decimal TotalAmount { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;

    public static ExportPaymentsToBankCsvPayload FromResponse(ExportPaymentsToBankCsvResponse response) =>
        new()
        {
            FileName = response.FileName,
            FileBase64 = response.FileBase64,
            ContentType = response.ContentType,
            RowCount = response.RowCount,
            TotalAmount = response.TotalAmount,
            CurrencyCode = response.CurrencyCode
        };
}
