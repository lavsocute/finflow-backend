using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Bank.Commands.ExportPaymentsToBankCsv;

public sealed record ExportPaymentsToBankCsvCommand(
    Guid TenantId,
    Guid AccountantAccountId,
    Guid AccountantMembershipId,
    IReadOnlyList<Guid> PaymentIds,
    string BankFormat
) : ICommand<Result<ExportPaymentsToBankCsvResponse>>;

public sealed record ExportPaymentsToBankCsvResponse(
    string FileName,
    string FileBase64,
    string ContentType,
    int RowCount,
    decimal TotalAmount,
    string CurrencyCode);
