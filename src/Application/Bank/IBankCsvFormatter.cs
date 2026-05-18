namespace FinFlow.Application.Bank;

/// <summary>
/// Strategy for rendering a list of payment rows into a bank-specific CSV
/// (or CSV-like) file that the corresponding corporate banking platform
/// accepts as a bulk-transfer upload.
///
/// Implementations are pure — no DI on infrastructure, no I/O. They take rows in
/// and return a single string representing the file content. The handler wraps
/// the output with metadata (file name, base64 encoding, audit log).
/// </summary>
public interface IBankCsvFormatter
{
    /// <summary>Stable identifier used by frontend dropdowns and audit logs.</summary>
    string FormatCode { get; }

    /// <summary>Human-readable name shown to accountant.</summary>
    string FormatName { get; }

    /// <summary>File extension including the dot. Currently always <c>.csv</c>.</summary>
    string FileExtension { get; }

    /// <summary>Hint for UI: <c>,</c> or <c>;</c> — used to render preview correctly.</summary>
    string Separator { get; }

    /// <summary>
    /// Render the rows. Implementations must be deterministic for given input
    /// (identical bytes for identical row sequence) so integration tests can
    /// assert byte-exact output.
    /// </summary>
    string Format(IReadOnlyList<PaymentExportRow> rows);
}

/// <summary>
/// Snapshot of the data each row in the exported file represents. The handler
/// builds this by joining payment + reviewed document + employee reimbursement
/// profile, decrypting the bank account number along the way.
/// </summary>
public sealed record PaymentExportRow(
    int Sequence,
    string PayeeName,
    string PayeeBankCode,
    string PayeeBankAccountNumber,
    string? PayeeBankBranch,
    decimal Amount,
    string CurrencyCode,
    string TransferNote,
    string ExportReference);
