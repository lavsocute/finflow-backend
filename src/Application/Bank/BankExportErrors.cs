using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Bank;

public static class BankExportErrors
{
    public static readonly Error UnknownFormat =
        new("BankExport.UnknownFormat", "Bank format is not supported.");

    public static readonly Error InvalidRowCount =
        new("BankExport.InvalidRowCount", "Number of payments must be between 1 and 200.");

    public static readonly Error SomePaymentsNotFound =
        new("BankExport.SomePaymentsNotFound", "One or more payments not found in current tenant.");

    public static readonly Error NotAllPending =
        new("BankExport.NotAllPending", "All payments must be in Pending status to export.");

    public static readonly Error NotAllBankTransfer =
        new("BankExport.NotAllBankTransfer", "Only payments with method BankTransfer can be exported.");

    public static readonly Error MixedCurrencies =
        new("BankExport.MixedCurrencies", "All payments must share the same currency in a single export.");

    public static Error MissingBankInfo(IReadOnlyList<Guid> membershipIds) =>
        new("BankExport.MissingBankInfo",
            $"Some employees have not configured bank info: {string.Join(", ", membershipIds.Select(id => id.ToString()[..8]))}");

    public static Error DocumentNotFound(IReadOnlyList<Guid> documentIds) =>
        new("BankExport.DocumentNotFound",
            $"Some payment documents not accessible: {string.Join(", ", documentIds.Select(id => id.ToString()[..8]))}");
}
