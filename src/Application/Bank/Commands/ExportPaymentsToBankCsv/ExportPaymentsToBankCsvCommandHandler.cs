using System.Globalization;
using System.Text;
using System.Text.Json;
using FinFlow.Application.Common.Security;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Audit;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Employees;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.TenantMemberships;
using MediatR;

namespace FinFlow.Application.Bank.Commands.ExportPaymentsToBankCsv;

/// <summary>
/// Builds a single bank-format CSV file from N pending BankTransfer payments. The
/// accountant uploads the resulting file to their corporate banking platform
/// (VCB Cash Manager, BIDV iBank, Techcombank Business, ...). The handler does
/// NOT mark payments as confirmed — accountant returns to FinFlow afterwards and
/// confirms each payment with the bank's reference number per existing flow.
///
/// Hard rules enforced here:
///  - Caller must be Accountant or TenantAdmin (enforced at GraphQL layer)
///  - 1..200 payments per export
///  - All payments must be Status=Pending and Method=BankTransfer
///  - All payments must share the same currency
///  - Every employee must have <see cref="EmployeeReimbursementProfile.HasBankInfo"/>
///  - Plaintext bank account numbers are NEVER written to logs / audit / errors
///    (only last-4 reference and short-hash payment IDs)
/// </summary>
internal sealed class ExportPaymentsToBankCsvCommandHandler
    : IRequestHandler<ExportPaymentsToBankCsvCommand, Result<ExportPaymentsToBankCsvResponse>>
{
    private const int MaxRowsPerExport = 200;
    private const string ExportAuditAction = "PAYMENTS_EXPORTED_TO_CSV";
    private const string ProfileAccessAuditAction = "EMPLOYEE_BANK_INFO_ACCESSED";
    private const string ProfileEntityType = "EmployeeReimbursementProfile";
    private const string ExportEntityType = "PaymentExport";

    private readonly BankCsvFormatterRegistry _formatterRegistry;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IReviewedDocumentRepository _documentRepository;
    private readonly IEmployeeReimbursementProfileRepository _profileRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IPiiEncryptionService _piiEncryption;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ExportPaymentsToBankCsvCommandHandler(
        BankCsvFormatterRegistry formatterRegistry,
        IPaymentRepository paymentRepository,
        IReviewedDocumentRepository documentRepository,
        IEmployeeReimbursementProfileRepository profileRepository,
        ITenantMembershipRepository membershipRepository,
        IAccountRepository accountRepository,
        IPiiEncryptionService piiEncryption,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork)
    {
        _formatterRegistry = formatterRegistry;
        _paymentRepository = paymentRepository;
        _documentRepository = documentRepository;
        _profileRepository = profileRepository;
        _membershipRepository = membershipRepository;
        _accountRepository = accountRepository;
        _piiEncryption = piiEncryption;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ExportPaymentsToBankCsvResponse>> Handle(
        ExportPaymentsToBankCsvCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Format lookup
        var formatter = _formatterRegistry.Find(request.BankFormat);
        if (formatter is null)
            return Result.Failure<ExportPaymentsToBankCsvResponse>(BankExportErrors.UnknownFormat);

        // 2. Row-count guard
        if (request.PaymentIds is null || request.PaymentIds.Count is < 1 or > MaxRowsPerExport)
            return Result.Failure<ExportPaymentsToBankCsvResponse>(BankExportErrors.InvalidRowCount);

        // De-dup but keep original order
        var distinctIds = request.PaymentIds.Distinct().ToList();
        if (distinctIds.Count != request.PaymentIds.Count)
            return Result.Failure<ExportPaymentsToBankCsvResponse>(BankExportErrors.InvalidRowCount);

        // 3. Bulk-load payments scoped to tenant — count mismatch implies foreign tenant ID
        var payments = await _paymentRepository.GetByIdsAsync(distinctIds, request.TenantId, cancellationToken);
        if (payments.Count != distinctIds.Count)
            return Result.Failure<ExportPaymentsToBankCsvResponse>(BankExportErrors.SomePaymentsNotFound);

        // 4. State validation
        if (payments.Any(p => p.Status != PaymentStatus.Pending))
            return Result.Failure<ExportPaymentsToBankCsvResponse>(BankExportErrors.NotAllPending);
        if (payments.Any(p => p.Method != PaymentMethod.BankTransfer))
            return Result.Failure<ExportPaymentsToBankCsvResponse>(BankExportErrors.NotAllBankTransfer);

        var distinctCurrencies = payments.Select(p => p.CurrencyCode).Distinct().ToList();
        if (distinctCurrencies.Count > 1)
            return Result.Failure<ExportPaymentsToBankCsvResponse>(BankExportErrors.MixedCurrencies);

        // 5. Resolve documents → membership IDs (the employee being reimbursed)
        var documentIds = payments.Select(p => p.DocumentId).Distinct().ToList();
        var documents = await _documentRepository.GetByIdsAsync(documentIds, request.TenantId, cancellationToken);
        if (documents.Count != documentIds.Count)
            return Result.Failure<ExportPaymentsToBankCsvResponse>(
                BankExportErrors.DocumentNotFound(documentIds.Except(documents.Select(d => d.Id)).ToList()));

        var documentByIdMap = documents.ToDictionary(d => d.Id);

        var membershipIds = documents.Select(d => d.MembershipId).Distinct().ToList();

        // 6. Profiles + memberships + accounts (bulk)
        var profiles = await _profileRepository.GetByMembershipIdsAsync(membershipIds, cancellationToken);
        var profileByMembershipMap = profiles.ToDictionary(p => p.MembershipId);

        var missingMemberships = membershipIds
            .Where(mid => !profileByMembershipMap.TryGetValue(mid, out var p) || !p.HasBankInfo)
            .ToList();
        if (missingMemberships.Count > 0)
            return Result.Failure<ExportPaymentsToBankCsvResponse>(BankExportErrors.MissingBankInfo(missingMemberships));

        var memberships = await _membershipRepository.GetByIdsAsync(membershipIds, cancellationToken);
        var membershipById = memberships.ToDictionary(m => m.Id);

        var accountIds = memberships.Select(m => m.AccountId).Distinct().ToList();
        var accounts = await _accountRepository.GetByIdsAsync(accountIds, cancellationToken);
        var accountById = accounts.ToDictionary(a => a.Id);

        // 7. Build rows in the same order accountant submitted IDs (stable for accountant UX)
        var paymentByIdMap = payments.ToDictionary(p => p.Id);
        var rows = new List<PaymentExportRow>(distinctIds.Count);
        var sequence = 1;
        foreach (var id in distinctIds)
        {
            var payment = paymentByIdMap[id];
            var document = documentByIdMap[payment.DocumentId];
            var profile = profileByMembershipMap[document.MembershipId];

            var bank = VietnamBanks.Find(profile.BankCode!);
            if (bank is null)
                return Result.Failure<ExportPaymentsToBankCsvResponse>(ProfileErrors.UnsupportedBankCode);

            // Decrypt is wrapped — on failure we never leak ciphertext / partial state
            string accountNumber;
            try
            {
                accountNumber = _piiEncryption.Decrypt(profile.BankAccountNumberEncrypted!);
            }
            catch
            {
                // Don't leak which payment failed publicly — just halt the export.
                return Result.Failure<ExportPaymentsToBankCsvResponse>(BankExportErrors.MissingBankInfo([profile.MembershipId]));
            }

            var payeeName = !string.IsNullOrWhiteSpace(profile.BankAccountHolderName)
                ? profile.BankAccountHolderName!
                : ResolveAccountName(membershipById, accountById, document.MembershipId)
                  ?? "UNKNOWN";

            var exportReference = payment.Id.ToString("N")[..8].ToUpperInvariant();
            var transferNote = BuildTransferNote(exportReference, payeeName);

            rows.Add(new PaymentExportRow(
                Sequence: sequence++,
                PayeeName: payeeName,
                PayeeBankCode: bank.Code,
                PayeeBankAccountNumber: accountNumber,
                PayeeBankBranch: profile.BankBranch,
                Amount: payment.Amount,
                CurrencyCode: payment.CurrencyCode,
                TransferNote: transferNote,
                ExportReference: exportReference));
        }

        // 8. Render via formatter (deterministic, byte-stable)
        var content = formatter.Format(rows);
        var bytes = Encoding.UTF8.GetBytes(content);

        // 9. Audit emission
        await EmitAuditAsync(request, formatter, rows, profileByMembershipMap, documents, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var fileName = BuildFileName(formatter, rows.Count);

        return Result.Success(new ExportPaymentsToBankCsvResponse(
            FileName: fileName,
            FileBase64: Convert.ToBase64String(bytes),
            ContentType: $"text/csv; charset=utf-8",
            RowCount: rows.Count,
            TotalAmount: rows.Sum(r => r.Amount),
            CurrencyCode: distinctCurrencies.Single()));
    }

    private static string? ResolveAccountName(
        IReadOnlyDictionary<Guid, TenantMembershipSummary> membershipById,
        IReadOnlyDictionary<Guid, AccountSummary> accountById,
        Guid membershipId)
    {
        if (!membershipById.TryGetValue(membershipId, out var membership))
            return null;
        return accountById.TryGetValue(membership.AccountId, out var account)
            ? account.FullName
            : null;
    }

    /// <summary>
    /// Bank transfer notes have ~210-char limits across most VN banks. We keep ours
    /// short, ASCII-safe, and prefixed with an idempotency-friendly export reference
    /// so the accountant can reconcile back to the FinFlow payment row.
    /// </summary>
    private static string BuildTransferNote(string exportReference, string payeeName)
    {
        // Take first word of payee name (typically family name + given name; first
        // chunk is enough for matching). Strip diacritics for legacy ASCII-only banks.
        var firstWord = payeeName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var ascii = StripDiacritics(firstWord).ToUpperInvariant();
        if (ascii.Length > 16)
            ascii = ascii[..16];
        var note = $"REIMB {exportReference} {ascii}".Trim();
        return note.Length > 100 ? note[..100] : note;
    }

    private static string StripDiacritics(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        // Vietnamese đ/Đ are not decomposed by NFD — handle explicitly.
        return sb.ToString().Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormC);
    }

    private static string BuildFileName(IBankCsvFormatter formatter, int rowCount)
    {
        var now = DateTime.UtcNow;
        var formatCode = formatter.FormatCode.ToLowerInvariant();
        return $"finflow-payments-{formatCode}-{now:yyyyMMdd}-{now:HHmmss}-{rowCount}{formatter.FileExtension}";
    }

    private async Task EmitAuditAsync(
        ExportPaymentsToBankCsvCommand request,
        IBankCsvFormatter formatter,
        IReadOnlyList<PaymentExportRow> rows,
        IReadOnlyDictionary<Guid, EmployeeReimbursementProfile> profileByMembership,
        IReadOnlyList<ReviewedDocument> documents,
        CancellationToken cancellationToken)
    {
        // 1. Single export-level audit row (metadata only — never plaintext PII)
        var exportPayload = JsonSerializer.Serialize(new
        {
            format = formatter.FormatCode,
            rowCount = rows.Count,
            totalAmount = rows.Sum(r => r.Amount),
            currency = rows.Count > 0 ? rows[0].CurrencyCode : null,
            paymentReferences = rows.Select(r => r.ExportReference).ToArray()
        });
        var exportLog = AuditLog.Create(
            action: ExportAuditAction,
            entityType: ExportEntityType,
            entityId: null,
            newValue: exportPayload,
            idTenant: request.TenantId,
            idAccount: request.AccountantAccountId);
        await _auditLogRepository.AddAsync(exportLog, cancellationToken);

        // 2. One profile-access row per distinct employee (last-4 only — same as
        //    GetReimbursementProfileForPayoutQueryHandler emits)
        foreach (var doc in documents)
        {
            var profile = profileByMembership[doc.MembershipId];
            var accessPayload = JsonSerializer.Serialize(new
            {
                membershipId = profile.MembershipId,
                bankCode = profile.BankCode,
                last4 = profile.BankAccountLast4,
                via = "csv-export"
            });
            var accessLog = AuditLog.Create(
                action: ProfileAccessAuditAction,
                entityType: ProfileEntityType,
                entityId: profile.Id.ToString(),
                newValue: accessPayload,
                idTenant: request.TenantId,
                idAccount: request.AccountantAccountId);
            await _auditLogRepository.AddAsync(accessLog, cancellationToken);
        }
    }
}
