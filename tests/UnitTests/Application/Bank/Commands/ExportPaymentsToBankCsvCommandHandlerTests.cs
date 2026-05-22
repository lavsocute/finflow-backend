using System.Text;
using FinFlow.Application.Bank;
using FinFlow.Application.Bank.Commands.ExportPaymentsToBankCsv;
using FinFlow.Application.Bank.Formatters;
using FinFlow.Application.Common.Security;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Audit;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Employees;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.TenantMemberships;
using Xunit;

namespace FinFlow.UnitTests.Application.Bank.Commands;

/// <summary>
/// Behavior tests for the bank-CSV export handler. Covers validation paths
/// (size, currency, status, method, missing profile, foreign tenant), the audit
/// emission contract, and the file-name generation contract.
/// </summary>
public class ExportPaymentsToBankCsvCommandHandlerTests
{
    [Fact]
    public async Task UnknownFormat_ReturnsUnknownFormatError()
    {
        var fixture = TestFixture.Build();
        var (_, paymentIds) = fixture.CreateApprovedBankTransferPayments(2);

        var result = await fixture.Handler.Handle(
            new ExportPaymentsToBankCsvCommand(
                fixture.TenantId,
                fixture.AccountantAccountId,
                fixture.AccountantMembershipId,
                paymentIds,
                "DOES_NOT_EXIST"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BankExportErrors.UnknownFormat, result.Error);
    }

    [Fact]
    public async Task EmptyPaymentList_ReturnsInvalidRowCount()
    {
        var fixture = TestFixture.Build();

        var result = await fixture.Handler.Handle(
            new ExportPaymentsToBankCsvCommand(
                fixture.TenantId, fixture.AccountantAccountId, fixture.AccountantMembershipId,
                Array.Empty<Guid>(),
                "VCB"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BankExportErrors.InvalidRowCount, result.Error);
    }

    [Fact]
    public async Task TooManyPayments_ReturnsInvalidRowCount()
    {
        var fixture = TestFixture.Build();
        var ids = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList();

        var result = await fixture.Handler.Handle(
            new ExportPaymentsToBankCsvCommand(
                fixture.TenantId, fixture.AccountantAccountId, fixture.AccountantMembershipId,
                ids,
                "VCB"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BankExportErrors.InvalidRowCount, result.Error);
    }

    [Fact]
    public async Task DuplicatePaymentIds_ReturnsInvalidRowCount()
    {
        var fixture = TestFixture.Build();
        var (_, ids) = fixture.CreateApprovedBankTransferPayments(1);
        var dups = new List<Guid> { ids[0], ids[0] };

        var result = await fixture.Handler.Handle(
            new ExportPaymentsToBankCsvCommand(
                fixture.TenantId, fixture.AccountantAccountId, fixture.AccountantMembershipId,
                dups,
                "VCB"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BankExportErrors.InvalidRowCount, result.Error);
    }

    [Fact]
    public async Task ForeignTenantPaymentId_ReturnsSomePaymentsNotFound()
    {
        var fixture = TestFixture.Build();
        var (_, ids) = fixture.CreateApprovedBankTransferPayments(2);
        ids = [.. ids, Guid.NewGuid()];   // unknown id

        var result = await fixture.Handler.Handle(
            new ExportPaymentsToBankCsvCommand(
                fixture.TenantId, fixture.AccountantAccountId, fixture.AccountantMembershipId,
                ids,
                "VCB"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BankExportErrors.SomePaymentsNotFound, result.Error);
    }

    [Fact]
    public async Task NotAllPending_ReturnsNotAllPendingError()
    {
        var fixture = TestFixture.Build();
        var (payments, ids) = fixture.CreateApprovedBankTransferPayments(2);
        // Confirm one payment so it's no longer Pending.
        payments[0].Confirm(Guid.NewGuid(), "REF");

        var result = await fixture.Handler.Handle(
            new ExportPaymentsToBankCsvCommand(
                fixture.TenantId, fixture.AccountantAccountId, fixture.AccountantMembershipId,
                ids,
                "VCB"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BankExportErrors.NotAllPending, result.Error);
    }

    [Fact]
    public async Task NotAllBankTransfer_ReturnsNotAllBankTransferError()
    {
        var fixture = TestFixture.Build();
        var (_, ids1) = fixture.CreateApprovedBankTransferPayments(1);
        var (_, ids2) = fixture.CreateApprovedPayments(1, PaymentMethod.Cash);
        var ids = ids1.Concat(ids2).ToList();

        var result = await fixture.Handler.Handle(
            new ExportPaymentsToBankCsvCommand(
                fixture.TenantId, fixture.AccountantAccountId, fixture.AccountantMembershipId,
                ids,
                "VCB"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BankExportErrors.NotAllBankTransfer, result.Error);
    }

    [Fact]
    public async Task MixedCurrencies_ReturnsMixedCurrenciesError()
    {
        var fixture = TestFixture.Build();
        var (_, ids1) = fixture.CreateApprovedBankTransferPayments(1, currency: "VND");
        var (_, ids2) = fixture.CreateApprovedBankTransferPayments(1, currency: "USD", exchangeRate: 25_000m);
        var ids = ids1.Concat(ids2).ToList();

        var result = await fixture.Handler.Handle(
            new ExportPaymentsToBankCsvCommand(
                fixture.TenantId, fixture.AccountantAccountId, fixture.AccountantMembershipId,
                ids,
                "VCB"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BankExportErrors.MixedCurrencies, result.Error);
    }

    [Fact]
    public async Task EmployeeWithoutBankInfo_ReturnsMissingBankInfoError()
    {
        var fixture = TestFixture.Build();
        var (_, ids) = fixture.CreateApprovedBankTransferPayments(2, withBankInfo: false);

        var result = await fixture.Handler.Handle(
            new ExportPaymentsToBankCsvCommand(
                fixture.TenantId, fixture.AccountantAccountId, fixture.AccountantMembershipId,
                ids,
                "VCB"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("BankExport.MissingBankInfo", result.Error.Code);
    }

    [Fact]
    public async Task HappyPath_ReturnsBase64FileWithExpectedMetadata()
    {
        var fixture = TestFixture.Build();
        var (_, ids) = fixture.CreateApprovedBankTransferPayments(3);

        var result = await fixture.Handler.Handle(
            new ExportPaymentsToBankCsvCommand(
                fixture.TenantId, fixture.AccountantAccountId, fixture.AccountantMembershipId,
                ids,
                "VCB"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.RowCount);
        Assert.Equal("VND", result.Value.CurrencyCode);
        Assert.StartsWith("finflow-payments-vcb-", result.Value.FileName);
        Assert.EndsWith("-3.csv", result.Value.FileName);
        Assert.Equal("text/csv; charset=utf-8", result.Value.ContentType);

        var bytes = Convert.FromBase64String(result.Value.FileBase64);
        var content = Encoding.UTF8.GetString(bytes);
        Assert.Equal('\uFEFF', content[0]);                   // BOM survived round-trip
        Assert.Contains("STT,Ten nguoi nhan", content);
    }

    [Fact]
    public async Task HappyPath_EmitsOneExportAuditAndOneAccessAuditPerEmployee()
    {
        var fixture = TestFixture.Build();
        var (_, ids) = fixture.CreateApprovedBankTransferPayments(3);

        var result = await fixture.Handler.Handle(
            new ExportPaymentsToBankCsvCommand(
                fixture.TenantId, fixture.AccountantAccountId, fixture.AccountantMembershipId,
                ids,
                "VCB"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(fixture.AuditLogRepository.Logs.Where(l => l.Action == "PAYMENTS_EXPORTED_TO_CSV"));
        Assert.Equal(3, fixture.AuditLogRepository.Logs.Count(l => l.Action == "EMPLOYEE_BANK_INFO_ACCESSED"));
        // Total audit rows = 1 export + 3 access
        Assert.Equal(4, fixture.AuditLogRepository.Logs.Count);
    }

    [Fact]
    public async Task HappyPath_OutputContainsDecryptedAccountNumbers()
    {
        var fixture = TestFixture.Build();
        var (_, ids) = fixture.CreateApprovedBankTransferPayments(1);

        var result = await fixture.Handler.Handle(
            new ExportPaymentsToBankCsvCommand(
                fixture.TenantId, fixture.AccountantAccountId, fixture.AccountantMembershipId,
                ids,
                "GENERIC"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var content = Encoding.UTF8.GetString(Convert.FromBase64String(result.Value.FileBase64));
        // FakePiiEncryptionService returns "ACC-{seq}" as plaintext for decryption
        Assert.Contains("ACC-1", content);
    }

    // ─────────────────────────────────────────── fixture
    private sealed class TestFixture
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid AccountantAccountId { get; } = Guid.NewGuid();
        public Guid AccountantMembershipId { get; } = Guid.NewGuid();
        public Guid DepartmentId { get; } = Guid.NewGuid();

        public required ExportPaymentsToBankCsvCommandHandler Handler { get; init; }
        public required FakePaymentRepository PaymentRepository { get; init; }
        public required FakeReviewedDocumentRepository DocumentRepository { get; init; }
        public required FakeProfileRepository ProfileRepository { get; init; }
        public required FakeMembershipRepository MembershipRepository { get; init; }
        public required FakeAccountRepository AccountRepository { get; init; }
        public required FakeAuditLogRepository AuditLogRepository { get; init; }

        private int _seq;

        public static TestFixture Build()
        {
            IBankCsvFormatter[] formatters =
            [
                new VietcombankCsvFormatter(),
                new BidvBulkTransferCsvFormatter(),
                new TechcombankCsvFormatter(),
                new GenericCsvFormatter(),
            ];
            var registry = new BankCsvFormatterRegistry(formatters);

            var paymentRepo = new FakePaymentRepository();
            var docRepo = new FakeReviewedDocumentRepository();
            var profileRepo = new FakeProfileRepository();
            var membershipRepo = new FakeMembershipRepository();
            var accountRepo = new FakeAccountRepository();
            var auditRepo = new FakeAuditLogRepository();
            var pii = new FakePiiEncryptionService();
            var uow = new FakeUnitOfWork();

            var handler = new ExportPaymentsToBankCsvCommandHandler(
                registry,
                paymentRepo,
                docRepo,
                profileRepo,
                membershipRepo,
                accountRepo,
                pii,
                auditRepo,
                uow);

            return new TestFixture
            {
                Handler = handler,
                PaymentRepository = paymentRepo,
                DocumentRepository = docRepo,
                ProfileRepository = profileRepo,
                MembershipRepository = membershipRepo,
                AccountRepository = accountRepo,
                AuditLogRepository = auditRepo,
            };
        }

        public (List<Payment> Payments, List<Guid> Ids) CreateApprovedBankTransferPayments(
            int count,
            string currency = "VND",
            decimal exchangeRate = 1m,
            bool withBankInfo = true) =>
            CreateApprovedPayments(count, PaymentMethod.BankTransfer, currency, exchangeRate, withBankInfo);

        public (List<Payment> Payments, List<Guid> Ids) CreateApprovedPayments(
            int count,
            PaymentMethod method,
            string currency = "VND",
            decimal exchangeRate = 1m,
            bool withBankInfo = true)
        {
            var payments = new List<Payment>();
            var ids = new List<Guid>();
            for (var i = 0; i < count; i++)
            {
                _seq++;
                var membershipId = Guid.NewGuid();
                var docId = Guid.NewGuid();

                var profile = EmployeeReimbursementProfile.Create(TenantId, membershipId).Value;
                if (withBankInfo)
                {
                    var encryptedBlob = Encoding.UTF8.GetBytes($"ENC-{_seq}");
                    profile.UpdateBankInfo("VCB", encryptedBlob, "1234", $"NGUYEN VAN {(char)('A' + i)}", "HCM");
                }
                ProfileRepository.Add(profile);
                MembershipRepository.Add(new TenantMembershipSummary(
                    Id: membershipId,
                    AccountId: Guid.NewGuid(),
                    IdTenant: TenantId,
                    DepartmentId: DepartmentId,
                    Role: RoleType.Staff,
                    IsOwner: false,
                    IsActive: true,
                    CreatedAt: DateTime.UtcNow,
                    DeactivatedAt: null,
                    DeactivatedBy: null,
                    DeactivatedReason: null));

                // Account name (used as fallback if profile.BankAccountHolderName missing)
                var membership = MembershipRepository.LookupById(membershipId)!;
                AccountRepository.Add(new AccountSummary(membership.AccountId, $"emp{_seq}@x.test", $"NGUYEN VAN {(char)('A' + i)}", true, true, DateTime.UtcNow));

                DocumentRepository.Add(new FakeReviewedDocument(docId, TenantId, membershipId));

                var paymentResult = Payment.Create(
                    idTenant: TenantId,
                    documentId: docId,
                    idDepartment: DepartmentId,
                    amount: 100_000m,
                    currencyCode: currency,
                    exchangeRate: exchangeRate,
                    baseCurrencyCode: "VND",
                    recordedByMembershipId: AccountantMembershipId,
                    method: method,
                    notes: null);
                Assert.True(paymentResult.IsSuccess, paymentResult.IsFailure ? paymentResult.Error.Description : null);
                var payment = paymentResult.Value;
                PaymentRepository.Add(payment);
                payments.Add(payment);
                ids.Add(payment.Id);
            }
            return (payments, ids);
        }
    }

    // ─────────────────────────────────────────── fakes
    private sealed class FakePaymentRepository : IPaymentRepository
    {
        private readonly Dictionary<Guid, Payment> _byId = [];
        public void Add(Payment payment) => _byId[payment.Id] = payment;
        public void Update(Payment payment) => _byId[payment.Id] = payment;

        public Task<IReadOnlyList<Payment>> GetByIdsAsync(IReadOnlyList<Guid> ids, Guid tenantId, CancellationToken cancellationToken = default)
        {
            var list = ids
                .Where(id => _byId.TryGetValue(id, out var p) && p.IdTenant == tenantId)
                .Select(id => _byId[id])
                .ToList();
            return Task.FromResult<IReadOnlyList<Payment>>(list);
        }

        public Task<PaymentSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentSummary?> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PaymentSummary>> GetByTenantIdAsync(Guid idTenant, PaymentStatus? status = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PaymentSummary>> GetPendingByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Payment?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// Lightweight ReviewedDocument double — we can't easily build a real one for
    /// these tests because <see cref="ReviewedDocument.CreateSubmitted"/> requires
    /// many fields irrelevant to the export flow. Instead we wrap a hand-built doc
    /// in a fake repository that only exposes the fields the handler needs.
    /// </summary>
    private sealed record FakeReviewedDocument(Guid Id, Guid TenantId, Guid MembershipId);

    private sealed class FakeReviewedDocumentRepository : IReviewedDocumentRepository
    {
        private readonly Dictionary<Guid, FakeReviewedDocument> _byId = [];
        public void Add(FakeReviewedDocument doc) => _byId[doc.Id] = doc;

        public Task<IReadOnlyList<ReviewedDocument>> GetByIdsAsync(IReadOnlyList<Guid> ids, Guid tenantId, CancellationToken cancellationToken = default)
        {
            // Need to hydrate real ReviewedDocument instances. Use the public factory.
            var docs = new List<ReviewedDocument>();
            foreach (var id in ids)
            {
                if (!_byId.TryGetValue(id, out var fake) || fake.TenantId != tenantId)
                    continue;
                var doc = ReviewedDocument.CreateSubmitted(
                    documentId: fake.Id,
                    idTenant: fake.TenantId,
                    idDepartment: Guid.NewGuid(),
                    membershipId: fake.MembershipId,
                    originalFileName: "receipt.pdf",
                    contentType: "application/pdf",
                    vendorName: "Acme",
                    reference: $"INV-{Math.Abs(id.GetHashCode())}",
                    documentDate: new DateOnly(2026, 5, 1),
                    category: "Travel",
                    vendorTaxId: null,
                    subtotal: 100m,
                    vat: 0m,
                    totalAmount: 100m,
                    source: "staff-upload",
                    reviewedByStaff: "x@x.test",
                    confidenceLabel: "high",
                    submittedAtUtc: DateTime.UtcNow,
                    lineItems: [ReviewedDocumentLineItem.Create("Item", 1m, 100m, 100m)]
                ).Value;
                doc.Approve();
                docs.Add(doc);
            }
            return Task.FromResult<IReadOnlyList<ReviewedDocument>>(docs);
        }

        public void Add(ReviewedDocument document) => throw new NotSupportedException();
        public void Update(ReviewedDocument document) => throw new NotSupportedException();
        public Task<ReviewedDocument?> GetByIdForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReviewedDocument?> GetOwnedByIdAsync(Guid id, Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReviewedDocument>> GetAllActiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReviewedDocument>> GetReadyForApprovalAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReviewedDocument>> GetReadyForApprovalByDepartmentAsync(Guid tenantId, Guid departmentId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewedDocument>>(Array.Empty<ReviewedDocument>());
        public Task<IReadOnlyList<ReviewedDocument>> GetOwnedReadyForApprovalAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewedDocument>>(Array.Empty<ReviewedDocument>());
        public Task<IReadOnlyList<ReviewedDocument>> GetOwnedSubmittedAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReviewedDocument>> GetByStatusAsync(Guid tenantId, ApprovalStatusFilter status, string? search, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountByStatusAsync(Guid tenantId, ApprovalStatusFilter status, string? search, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeProfileRepository : IEmployeeReimbursementProfileRepository
    {
        private readonly Dictionary<Guid, EmployeeReimbursementProfile> _byMembership = [];
        public void Add(EmployeeReimbursementProfile profile) => _byMembership[profile.MembershipId] = profile;
        public void Update(EmployeeReimbursementProfile profile) => _byMembership[profile.MembershipId] = profile;

        public Task<EmployeeReimbursementProfile?> GetByMembershipIdAsync(Guid membershipId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byMembership.GetValueOrDefault(membershipId));

        public Task<IReadOnlyList<EmployeeReimbursementProfile>> GetByMembershipIdsAsync(IReadOnlyList<Guid> membershipIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmployeeReimbursementProfile>>(
                membershipIds.Where(_byMembership.ContainsKey).Select(id => _byMembership[id]).ToList());

        public Task<EmployeeReimbursementProfile?> GetByIdForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeMembershipRepository : ITenantMembershipRepository
    {
        private readonly Dictionary<Guid, TenantMembershipSummary> _byId = [];
        public void Add(TenantMembershipSummary m) => _byId[m.Id] = m;
        public TenantMembershipSummary? LookupById(Guid id) => _byId.GetValueOrDefault(id);

        public Task<IReadOnlyList<TenantMembershipSummary>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantMembershipSummary>>(ids.Where(_byId.ContainsKey).Select(id => _byId[id]).ToList());

        public Task<TenantMembershipSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TenantMembershipSummary?> GetActiveByAccountAndTenantAsync(Guid accountId, Guid idTenant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantMembershipSummary>> GetActiveByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantMembershipSummary>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantMembershipSummary>> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(Guid accountId, Guid idTenant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsOwnerByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasActiveMembersInDepartmentAsync(Guid tenantId, Guid departmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TenantMembership?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(TenantMembership membership) => throw new NotSupportedException();
        public void Update(TenantMembership membership) => throw new NotSupportedException();
    }

    private sealed class FakeAccountRepository : IAccountRepository
    {
        private readonly Dictionary<Guid, AccountSummary> _byId = [];
        public void Add(AccountSummary a) => _byId[a.Id] = a;

        public Task<AccountSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byId.GetValueOrDefault(id));

        public Task<IReadOnlyList<AccountSummary>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AccountSummary>>(ids.Where(_byId.ContainsKey).Select(id => _byId[id]).ToList());

        public Task<AccountLoginInfo?> GetLoginInfoByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountLoginInfo?> GetLoginInfoByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByEmailIgnoringTenantAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Account?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(Account account) => throw new NotSupportedException();
        public void Update(Account account) => throw new NotSupportedException();
        public void Remove(Account account) => throw new NotSupportedException();
    }

    private sealed class FakeAuditLogRepository : IAuditLogRepository
    {
        public List<AuditLog> Logs { get; } = [];
        public Task AddAsync(AuditLog log, CancellationToken cancellationToken = default)
        {
            Logs.Add(log);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Fake encryption: round-trips ENC-{seq} ↔ ACC-{seq} so handler tests can
    /// assert that decrypted output appears in the file.
    /// </summary>
    private sealed class FakePiiEncryptionService : IPiiEncryptionService
    {
        public byte[] Encrypt(string plaintext) => Encoding.UTF8.GetBytes(plaintext);
        public string Decrypt(byte[] ciphertext)
        {
            var raw = Encoding.UTF8.GetString(ciphertext);
            // Map "ENC-N" → "ACC-N" so we can verify decryption ran
            return raw.Replace("ENC-", "ACC-");
        }
        public string MaskLast4(string plaintext) => plaintext.Length >= 4 ? plaintext[^4..] : plaintext;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }
}
