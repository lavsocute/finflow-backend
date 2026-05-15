using FinFlow.Application.Payments.Queries.GetPaymentDetail;
using FinFlow.Application.Payments.Queries.GetPaymentQueue;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.TenantMemberships;

namespace FinFlow.UnitTests.Application.Payments;

public sealed class PaymentReadQueriesTests
{
    [Fact]
    public async Task PaymentQueue_Should_Hide_Method_For_ReadyToPay_Items()
    {
        var fixture = PaymentReadFixture.Create();
        var handler = fixture.CreateQueueHandler();

        var result = await handler.Handle(
            new GetPaymentQueueQuery(fixture.TenantId, "ALL", null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var readyItem = Assert.Single(result.Value.Where(x => x.QueueStatus == "ReadyToPay"));
        Assert.Equal("Sarah Mitchell", readyItem.EmployeeName);
        Assert.Equal("Vertex Cloud Services", readyItem.MerchantName);
        Assert.Null(readyItem.PaymentId);
        Assert.Null(readyItem.PaymentMethod);
    }

    [Fact]
    public async Task PaymentQueue_Should_Map_Payment_Statuses_To_Reimbursement_Queue_Statuses()
    {
        var fixture = PaymentReadFixture.Create();
        var handler = fixture.CreateQueueHandler();

        var result = await handler.Handle(
            new GetPaymentQueueQuery(fixture.TenantId, "ALL", null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var scheduled = Assert.Single(result.Value.Where(x => x.Reference == "EXP-2024-0035"));
        var paid = Assert.Single(result.Value.Where(x => x.Reference == "EXP-2024-0034"));
        var failed = Assert.Single(result.Value.Where(x => x.Reference == "EXP-2024-0033"));

        Assert.Equal("Scheduled", scheduled.QueueStatus);
        Assert.Equal("BankTransfer", scheduled.PaymentMethod);

        Assert.Equal("Paid", paid.QueueStatus);
        Assert.Equal("Payroll", paid.PaymentMethod);

        Assert.Equal("Failed", failed.QueueStatus);
        Assert.Equal("Cash", failed.PaymentMethod);
        Assert.Equal("Bank rejected payout", failed.RejectionReason);
    }

    [Fact]
    public async Task PaymentDetail_Should_Support_DocumentId_For_ReadyToPay_Items()
    {
        var fixture = PaymentReadFixture.Create();
        var handler = fixture.CreateDetailHandler();

        var result = await handler.Handle(
            new GetPaymentDetailQuery(fixture.TenantId, null, fixture.ReadyDocument.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var detail = result.Value;
        Assert.NotNull(detail);

        Assert.Null(detail.PaymentId);
        Assert.Equal("ReadyToPay", detail.QueueStatus);
        Assert.Equal("Sarah Mitchell", detail.EmployeeName);
        Assert.Equal("Vertex Cloud Services", detail.MerchantName);
        Assert.Null(detail.PaymentMethod);
        Assert.True(detail.MethodEditable);
        Assert.Null(detail.SettlementRef);
    }

    [Fact]
    public async Task PaymentDetail_Should_Load_Payment_Backend_Data_For_Scheduled_Items()
    {
        var fixture = PaymentReadFixture.Create();
        var handler = fixture.CreateDetailHandler();

        var result = await handler.Handle(
            new GetPaymentDetailQuery(fixture.TenantId, fixture.ScheduledPayment.Id, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var detail = result.Value;
        Assert.NotNull(detail);

        Assert.Equal(fixture.ScheduledPayment.Id, detail.PaymentId);
        Assert.Equal("Scheduled", detail.QueueStatus);
        Assert.Equal("Marcus Lee", detail.EmployeeName);
        Assert.Equal("Creative Agency XYZ", detail.MerchantName);
        Assert.Equal("BankTransfer", detail.PaymentMethod);
        Assert.False(detail.MethodEditable);
        Assert.NotNull(detail.SettlementRef);
        Assert.NotEmpty(detail.AuditTrail);
    }

    private sealed class PaymentReadFixture
    {
        private PaymentReadFixture(
            Guid tenantId,
            ReviewedDocument readyDocument,
            ReviewedDocument scheduledDocument,
            ReviewedDocument paidDocument,
            ReviewedDocument failedDocument,
            Payment scheduledPayment,
            Payment paidPayment,
            Payment failedPayment,
            StubReviewedDocumentRepository documentRepository,
            StubPaymentRepository paymentRepository,
            StubMembershipRepository membershipRepository,
            StubAccountRepository accountRepository,
            StubDepartmentRepository departmentRepository)
        {
            TenantId = tenantId;
            ReadyDocument = readyDocument;
            ScheduledDocument = scheduledDocument;
            PaidDocument = paidDocument;
            FailedDocument = failedDocument;
            ScheduledPayment = scheduledPayment;
            PaidPayment = paidPayment;
            FailedPayment = failedPayment;
            DocumentRepository = documentRepository;
            PaymentRepository = paymentRepository;
            MembershipRepository = membershipRepository;
            AccountRepository = accountRepository;
            DepartmentRepository = departmentRepository;
        }

        public Guid TenantId { get; }
        public ReviewedDocument ReadyDocument { get; }
        public ReviewedDocument ScheduledDocument { get; }
        public ReviewedDocument PaidDocument { get; }
        public ReviewedDocument FailedDocument { get; }
        public Payment ScheduledPayment { get; }
        public Payment PaidPayment { get; }
        public Payment FailedPayment { get; }
        public StubReviewedDocumentRepository DocumentRepository { get; }
        public StubPaymentRepository PaymentRepository { get; }
        public StubMembershipRepository MembershipRepository { get; }
        public StubAccountRepository AccountRepository { get; }
        public StubDepartmentRepository DepartmentRepository { get; }

        public GetPaymentQueueQueryHandler CreateQueueHandler() =>
            new(DocumentRepository, PaymentRepository, MembershipRepository, AccountRepository, DepartmentRepository);

        public GetPaymentDetailQueryHandler CreateDetailHandler() =>
            new(DocumentRepository, PaymentRepository, MembershipRepository, AccountRepository, DepartmentRepository);

        public static PaymentReadFixture Create()
        {
            var tenantId = Guid.NewGuid();
            var engineeringDeptId = Guid.NewGuid();
            var marketingDeptId = Guid.NewGuid();
            var financeDeptId = Guid.NewGuid();
            var opsDeptId = Guid.NewGuid();

            var sarahMembershipId = Guid.NewGuid();
            var marcusMembershipId = Guid.NewGuid();
            var jordanMembershipId = Guid.NewGuid();
            var morganMembershipId = Guid.NewGuid();

            var sarahAccountId = Guid.NewGuid();
            var marcusAccountId = Guid.NewGuid();
            var jordanAccountId = Guid.NewGuid();
            var morganAccountId = Guid.NewGuid();

            var readyDocument = CreateApprovedDocument(
                tenantId,
                engineeringDeptId,
                sarahMembershipId,
                "Vertex Cloud Services",
                "EXP-2024-0039",
                new DateOnly(2024, 11, 22),
                9450m,
                submittedAtUtc: new DateTime(2024, 11, 17, 10, 42, 0, DateTimeKind.Utc));

            var scheduledDocument = CreateApprovedDocument(
                tenantId,
                marketingDeptId,
                marcusMembershipId,
                "Creative Agency XYZ",
                "EXP-2024-0035",
                new DateOnly(2024, 11, 25),
                7600m,
                submittedAtUtc: new DateTime(2024, 11, 18, 9, 5, 0, DateTimeKind.Utc));

            var paidDocument = CreateApprovedDocument(
                tenantId,
                opsDeptId,
                jordanMembershipId,
                "Northwind Security",
                "EXP-2024-0034",
                new DateOnly(2024, 11, 21),
                5100m,
                submittedAtUtc: new DateTime(2024, 11, 16, 8, 30, 0, DateTimeKind.Utc));

            var failedDocument = CreateApprovedDocument(
                tenantId,
                financeDeptId,
                morganMembershipId,
                "Atlas Hardware",
                "EXP-2024-0033",
                new DateOnly(2024, 11, 20),
                3200m,
                submittedAtUtc: new DateTime(2024, 11, 15, 13, 0, 0, DateTimeKind.Utc));

            var scheduledPayment = CreatePendingPayment(
                tenantId,
                scheduledDocument.Id,
                marketingDeptId,
                7600m,
                PaymentMethod.BankTransfer,
                recordedByMembershipId: Guid.NewGuid(),
                notes: "Scheduled for next payroll window");

            var paidPayment = CreatePendingPayment(
                tenantId,
                paidDocument.Id,
                opsDeptId,
                5100m,
                PaymentMethod.Payroll,
                recordedByMembershipId: Guid.NewGuid(),
                notes: "Included in payroll batch");
            paidPayment.Confirm(Guid.NewGuid(), "BANK-REF-001");

            var failedPayment = CreatePendingPayment(
                tenantId,
                failedDocument.Id,
                financeDeptId,
                3200m,
                PaymentMethod.Cash,
                recordedByMembershipId: Guid.NewGuid(),
                notes: "Cash reimbursement attempt");
            failedPayment.Reject(Guid.NewGuid(), PaymentRejectType.Other, "Bank rejected payout");

            var documentRepository = new StubReviewedDocumentRepository(
                [readyDocument, scheduledDocument, paidDocument, failedDocument]);

            var paymentRepository = new StubPaymentRepository(
                [scheduledPayment, paidPayment, failedPayment]);

            var membershipRepository = new StubMembershipRepository(
                new Dictionary<Guid, TenantMembershipSummary>
                {
                    [sarahMembershipId] = new(sarahMembershipId, sarahAccountId, tenantId, engineeringDeptId, RoleType.Staff, false, true, DateTime.UtcNow, null, null, null),
                    [marcusMembershipId] = new(marcusMembershipId, marcusAccountId, tenantId, marketingDeptId, RoleType.Staff, false, true, DateTime.UtcNow, null, null, null),
                    [jordanMembershipId] = new(jordanMembershipId, jordanAccountId, tenantId, opsDeptId, RoleType.Staff, false, true, DateTime.UtcNow, null, null, null),
                    [morganMembershipId] = new(morganMembershipId, morganAccountId, tenantId, financeDeptId, RoleType.Staff, false, true, DateTime.UtcNow, null, null, null),
                });

            var accountRepository = new StubAccountRepository(
                new Dictionary<Guid, AccountSummary>
                {
                    [sarahAccountId] = new(sarahAccountId, "sarah@finflow.test", "Sarah Mitchell", true, true, DateTime.UtcNow),
                    [marcusAccountId] = new(marcusAccountId, "marcus@finflow.test", "Marcus Lee", true, true, DateTime.UtcNow),
                    [jordanAccountId] = new(jordanAccountId, "jordan@finflow.test", "Jordan Lee", true, true, DateTime.UtcNow),
                    [morganAccountId] = new(morganAccountId, "morgan@finflow.test", "Morgan Chen", true, true, DateTime.UtcNow),
                });

            var departmentRepository = new StubDepartmentRepository(
                new Dictionary<Guid, DepartmentSummary>
                {
                    [engineeringDeptId] = new(engineeringDeptId, "Engineering", tenantId, null, true),
                    [marketingDeptId] = new(marketingDeptId, "Marketing", tenantId, null, true),
                    [opsDeptId] = new(opsDeptId, "Operations", tenantId, null, true),
                    [financeDeptId] = new(financeDeptId, "Finance", tenantId, null, true),
                });

            return new PaymentReadFixture(
                tenantId,
                readyDocument,
                scheduledDocument,
                paidDocument,
                failedDocument,
                scheduledPayment,
                paidPayment,
                failedPayment,
                documentRepository,
                paymentRepository,
                membershipRepository,
                accountRepository,
                departmentRepository);
        }

        private static ReviewedDocument CreateApprovedDocument(
            Guid tenantId,
            Guid departmentId,
            Guid membershipId,
            string vendorName,
            string reference,
            DateOnly documentDate,
            decimal totalAmount,
            DateTime submittedAtUtc)
        {
            var lineItem = ReviewedDocumentLineItem.Create("Expense line", 1m, totalAmount, totalAmount);

            var document = ReviewedDocument.CreateSubmitted(
                Guid.NewGuid(),
                tenantId,
                departmentId,
                membershipId,
                $"{reference}.pdf",
                "application/pdf",
                vendorName,
                reference,
                documentDate,
                "Travel",
                null,
                totalAmount,
                0m,
                totalAmount,
                "staff-upload",
                "staff@finflow.test",
                "Staff corrected",
                submittedAtUtc,
                [lineItem]).Value;

            document.Approve();
            return document;
        }

        private static Payment CreatePendingPayment(
            Guid tenantId,
            Guid documentId,
            Guid departmentId,
            decimal amount,
            PaymentMethod method,
            Guid recordedByMembershipId,
            string? notes)
        {
            return Payment.Create(
                tenantId,
                documentId,
                departmentId,
                amount,
                CurrencyCode.VND,
                1m,
                recordedByMembershipId,
                method,
                notes).Value;
        }
    }

    private sealed class StubReviewedDocumentRepository(IReadOnlyList<ReviewedDocument> documents) : IReviewedDocumentRepository
    {
        public void Add(ReviewedDocument document) => throw new NotSupportedException();
        public void Update(ReviewedDocument document) => throw new NotSupportedException();
        public Task<ReviewedDocument?> GetByIdForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(documents.FirstOrDefault(x => x.Id == id && x.IdTenant == tenantId));
        public Task<ReviewedDocument?> GetOwnedByIdAsync(Guid id, Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReviewedDocument>> GetAllActiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReviewedDocument>> GetReadyForApprovalAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReviewedDocument>> GetOwnedSubmittedAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReviewedDocument>> GetByStatusAsync(Guid tenantId, ApprovalStatusFilter status, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            IEnumerable<ReviewedDocument> query = documents.Where(x => x.IdTenant == tenantId);

            if (status == ApprovalStatusFilter.Approved)
            {
                query = query.Where(x => x.Status == ReviewedDocumentStatus.Approved);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var normalized = search.Trim().ToLowerInvariant();
                query = query.Where(x =>
                    x.VendorName.ToLowerInvariant().Contains(normalized) ||
                    x.Reference.ToLowerInvariant().Contains(normalized));
            }

            return Task.FromResult<IReadOnlyList<ReviewedDocument>>(query.ToList());
        }

        public Task<int> CountByStatusAsync(Guid tenantId, ApprovalStatusFilter status, string? search, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubPaymentRepository(IReadOnlyList<Payment> payments) : IPaymentRepository
    {
        public Task<PaymentSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(payments.Where(x => x.Id == id).Select(ToSummary).FirstOrDefault());

        public Task<PaymentSummary?> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(payments.Where(x => x.DocumentId == documentId).Select(ToSummary).FirstOrDefault());

        public Task<IReadOnlyList<PaymentSummary>> GetByTenantIdAsync(Guid idTenant, PaymentStatus? status = null, CancellationToken cancellationToken = default)
        {
            IEnumerable<Payment> query = payments.Where(x => x.IdTenant == idTenant);
            if (status.HasValue)
            {
                query = query.Where(x => x.Status == status.Value);
            }

            return Task.FromResult<IReadOnlyList<PaymentSummary>>(query.Select(ToSummary).ToList());
        }

        public Task<IReadOnlyList<PaymentSummary>> GetPendingByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default) =>
            GetByTenantIdAsync(idTenant, PaymentStatus.Pending, cancellationToken);

        public Task<bool> ExistsByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(payments.Any(x => x.DocumentId == documentId));

        public Task<Payment?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(Payment payment) => throw new NotSupportedException();
        public void Update(Payment payment) => throw new NotSupportedException();

        private static PaymentSummary ToSummary(Payment payment) =>
            new(
                payment.Id,
                payment.IdTenant,
                payment.DocumentId,
                payment.IdDepartment,
                payment.Amount,
                payment.CurrencyCode,
                payment.ExchangeRate,
                payment.AmountInVnd,
                payment.RecordedByMembershipId,
                payment.RecordedAt,
                payment.Method,
                payment.Status,
                payment.ConfirmedByMembershipId,
                payment.ConfirmedAt,
                payment.RejectionReason,
                payment.Notes,
                payment.CreatedAt);
    }

    private sealed class StubMembershipRepository(Dictionary<Guid, TenantMembershipSummary> memberships) : ITenantMembershipRepository
    {
        public Task<TenantMembershipSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(memberships.TryGetValue(id, out var value) ? value : null);

        public Task<IReadOnlyList<TenantMembershipSummary>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantMembershipSummary>>(ids.Where(memberships.ContainsKey).Select(id => memberships[id]).ToList());

        public Task<TenantMembershipSummary?> GetActiveByAccountAndTenantAsync(Guid accountId, Guid idTenant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantMembershipSummary>> GetActiveByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantMembershipSummary>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantMembershipSummary>> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(Guid accountId, Guid idTenant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsOwnerByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TenantMembership?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(TenantMembership membership) => throw new NotSupportedException();
        public void Update(TenantMembership membership) => throw new NotSupportedException();
    }

    private sealed class StubAccountRepository(Dictionary<Guid, AccountSummary> accounts) : IAccountRepository
    {
        public Task<AccountSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(accounts.TryGetValue(id, out var value) ? value : null);

        public Task<AccountLoginInfo?> GetLoginInfoByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountLoginInfo?> GetLoginInfoByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByEmailIgnoringTenantAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Account?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(Account account) => throw new NotSupportedException();
        public void Update(Account account) => throw new NotSupportedException();
        public void Remove(Account account) => throw new NotSupportedException();
    }

    private sealed class StubDepartmentRepository(Dictionary<Guid, DepartmentSummary> departments) : IDepartmentRepository
    {
        public Task<DepartmentSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(departments.TryGetValue(id, out var value) ? value : null);

        public Task<IReadOnlyList<DepartmentSummary>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DepartmentSummary>>(ids.Where(departments.ContainsKey).Select(id => departments[id]).ToList());

        public Task<DepartmentSummary?> GetDefaultByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DepartmentSummary>> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Department?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(Department department) => throw new NotSupportedException();
        public void Update(Department department) => throw new NotSupportedException();
        public void Remove(Department department) => throw new NotSupportedException();
    }
}
