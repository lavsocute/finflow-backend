using FinFlow.Application.Payments.Commands.ConfirmPayment;
using FinFlow.Application.Payments.Commands.RecordPayment;
using FinFlow.Application.Payments.Commands.RejectPayment;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;

namespace FinFlow.UnitTests.Application.Payments;

public sealed class PaymentMutationSemanticsTests
{
    [Fact]
    public async Task RecordPayment_Should_Not_AutoConfirm_LowValue_Reimbursement()
    {
        var fixture = MutationFixture.Create();
        var handler = fixture.CreateRecordHandler();

        var result = await handler.Handle(
            new RecordPaymentCommand(fixture.Document.Id, "BankTransfer", "schedule payout"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(fixture.PaymentRepository.AddedPayment);
        Assert.Equal(PaymentStatus.Pending, fixture.PaymentRepository.AddedPayment!.Status);
        Assert.Null(fixture.PaymentRepository.AddedPayment.ConfirmedAt);
        Assert.Empty(fixture.ExpenseRepository.AddedExpenses);
    }

    [Fact]
    public async Task ConfirmPayment_Should_Persist_ExecutionReference()
    {
        var fixture = MutationFixture.CreateWithScheduledPayment();
        var handler = fixture.CreateConfirmHandler();

        var result = await handler.Handle(
            new ConfirmPaymentCommand(fixture.ScheduledPayment!.Id, "BANK-REF-2026-001"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Confirmed, fixture.ScheduledPayment!.Status);
        Assert.Equal("BANK-REF-2026-001", fixture.ScheduledPayment.ExecutionReference);
    }

    [Fact]
    public async Task RejectPayment_Should_Require_Reason_When_Type_Is_Other()
    {
        var fixture = MutationFixture.CreateWithScheduledPayment();
        var handler = fixture.CreateRejectHandler();

        var result = await handler.Handle(
            new RejectPaymentCommand(fixture.ScheduledPayment!.Id, PaymentRejectType.Other, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Payment.RejectionReasonRequired", result.Error.Code);
    }

    [Fact]
    public async Task RejectPayment_Should_Persist_Structured_Type_And_Optional_Reason()
    {
        var fixture = MutationFixture.CreateWithScheduledPayment();
        var handler = fixture.CreateRejectHandler();

        var result = await handler.Handle(
            new RejectPaymentCommand(
                fixture.ScheduledPayment!.Id,
                PaymentRejectType.DuplicateClaim,
                "Duplicate reimbursement request"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Rejected, fixture.ScheduledPayment!.Status);
        Assert.Equal(PaymentRejectType.DuplicateClaim, fixture.ScheduledPayment.RejectionType);
        Assert.Equal("Duplicate reimbursement request", fixture.ScheduledPayment.RejectionReason);
    }

    private sealed class MutationFixture
    {
        private MutationFixture(
            Guid tenantId,
            Guid membershipId,
            ReviewedDocument document,
            StubPaymentRepository paymentRepository,
            StubExpenseRepository expenseRepository,
            StubBudgetRepository budgetRepository,
            StubCategoryRepository categoryRepository,
            StubReviewedDocumentRepository documentRepository,
            StubCurrentTenant currentTenant,
            StubUnitOfWork unitOfWork,
            Payment? scheduledPayment)
        {
            TenantId = tenantId;
            MembershipId = membershipId;
            Document = document;
            PaymentRepository = paymentRepository;
            ExpenseRepository = expenseRepository;
            BudgetRepository = budgetRepository;
            CategoryRepository = categoryRepository;
            DocumentRepository = documentRepository;
            CurrentTenant = currentTenant;
            UnitOfWork = unitOfWork;
            ScheduledPayment = scheduledPayment;
        }

        public Guid TenantId { get; }
        public Guid MembershipId { get; }
        public ReviewedDocument Document { get; }
        public Payment? ScheduledPayment { get; }
        public StubPaymentRepository PaymentRepository { get; }
        public StubExpenseRepository ExpenseRepository { get; }
        public StubBudgetRepository BudgetRepository { get; }
        public StubCategoryRepository CategoryRepository { get; }
        public StubReviewedDocumentRepository DocumentRepository { get; }
        public StubCurrentTenant CurrentTenant { get; }
        public StubUnitOfWork UnitOfWork { get; }

        public RecordPaymentCommandHandler CreateRecordHandler() =>
            new(
                PaymentRepository,
                ExpenseRepository,
                DocumentRepository,
                BudgetRepository,
                CategoryRepository,
                new NullReimbursementProfileRepository(),
                CurrentTenant,
                UnitOfWork);

        public ConfirmPaymentCommandHandler CreateConfirmHandler() =>
            new(
                PaymentRepository,
                ExpenseRepository,
                DocumentRepository,
                CategoryRepository,
                new NoOpBudgetReservationService(),
                CurrentTenant,
                UnitOfWork);

        public RejectPaymentCommandHandler CreateRejectHandler() =>
            new(PaymentRepository, DocumentRepository, new NoOpBudgetReservationService(), CurrentTenant, UnitOfWork);

        public static MutationFixture Create()
        {
            var tenantId = Guid.NewGuid();
            var membershipId = Guid.NewGuid();
            var departmentId = Guid.NewGuid();
            var document = CreateApprovedDocument(tenantId, membershipId, departmentId, 1000m);

            var paymentRepository = new StubPaymentRepository();
            var expenseRepository = new StubExpenseRepository();
            var budgetRepository = new StubBudgetRepository();
            var categoryRepository = new StubCategoryRepository();
            var documentRepository = new StubReviewedDocumentRepository(document);
            var currentTenant = new StubCurrentTenant { Id = tenantId, MembershipId = membershipId };
            var unitOfWork = new StubUnitOfWork();

            return new MutationFixture(
                tenantId,
                membershipId,
                document,
                paymentRepository,
                expenseRepository,
                budgetRepository,
                categoryRepository,
                documentRepository,
                currentTenant,
                unitOfWork,
                null);
        }

        public static MutationFixture CreateWithScheduledPayment()
        {
            var fixture = Create();
            var payment = Payment.Create(
                fixture.TenantId,
                fixture.Document.Id,
                fixture.Document.IdDepartment,
                fixture.Document.TotalAmount,
                "VND",
                1m,
                "VND",
                fixture.MembershipId,
                PaymentMethod.BankTransfer,
                "scheduled").Value;

            fixture.PaymentRepository.StoredById[payment.Id] = payment;
            fixture.PaymentRepository.StoredByDocumentId[payment.DocumentId] = payment;

            return new MutationFixture(
                fixture.TenantId,
                fixture.MembershipId,
                fixture.Document,
                fixture.PaymentRepository,
                fixture.ExpenseRepository,
                fixture.BudgetRepository,
                fixture.CategoryRepository,
                fixture.DocumentRepository,
                fixture.CurrentTenant,
                fixture.UnitOfWork,
                payment);
        }

        private static ReviewedDocument CreateApprovedDocument(Guid tenantId, Guid membershipId, Guid departmentId, decimal totalAmount)
        {
            var document = ReviewedDocument.CreateSubmitted(
                Guid.NewGuid(),
                tenantId,
                departmentId,
                membershipId,
                "receipt.pdf",
                "application/pdf",
                "Merchant",
                "EXP-001",
                new DateOnly(2026, 5, 1),
                "Travel",
                null,
                totalAmount,
                0m,
                totalAmount,
                "staff-upload",
                "staff@finflow.test",
                "Staff corrected",
                DateTime.UtcNow,
                [ReviewedDocumentLineItem.Create("Taxi", 1m, totalAmount, totalAmount)]).Value;

            document.Approve();
            return document;
        }
    }

    private sealed class StubPaymentRepository : IPaymentRepository
    {
        public Payment? AddedPayment { get; private set; }
        public Dictionary<Guid, Payment> StoredById { get; } = [];
        public Dictionary<Guid, Payment> StoredByDocumentId { get; } = [];

        public Task<PaymentSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(StoredById.TryGetValue(id, out var payment) ? ToSummary(payment) : null);

        public Task<PaymentSummary?> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(StoredByDocumentId.TryGetValue(documentId, out var payment) ? ToSummary(payment) : null);

        public Task<IReadOnlyList<PaymentSummary>> GetByTenantIdAsync(Guid idTenant, PaymentStatus? status = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PaymentSummary>> GetPendingByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(StoredByDocumentId.ContainsKey(documentId));

        public Task<Payment?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(StoredById.TryGetValue(id, out var payment) ? payment : null);

        public Task<IReadOnlyList<Payment>> GetByIdsAsync(IReadOnlyList<Guid> ids, Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Payment>>(StoredById.Values.Where(p => ids.Contains(p.Id) && p.IdTenant == tenantId).ToList());

        public void Add(Payment payment)
        {
            AddedPayment = payment;
            StoredById[payment.Id] = payment;
            StoredByDocumentId[payment.DocumentId] = payment;
        }

        public void Update(Payment payment)
        {
            StoredById[payment.Id] = payment;
            StoredByDocumentId[payment.DocumentId] = payment;
        }

        private static PaymentSummary ToSummary(Payment payment) =>
            new(
                payment.Id,
                payment.IdTenant,
                payment.DocumentId,
                payment.IdDepartment,
                payment.Amount,
                payment.CurrencyCode,
                payment.ExchangeRate,
                payment.AmountInBaseCurrency,
                payment.BaseCurrencyCode,
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

    private sealed class StubExpenseRepository : IExpenseRepository
    {
        public List<Expense> AddedExpenses { get; } = [];
        public Task<ExpenseSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseSummary?> GetByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExpenseSummary>> GetByDepartmentAndPeriodAsync(Guid departmentId, int month, int year, ExpenseStatus? status = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<decimal> GetTotalSpentByDepartmentAndPeriodAsync(Guid departmentId, int month, int year, ExpenseStatus status, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Expense?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(Expense expense) => AddedExpenses.Add(expense);
        public void Update(Expense expense) => throw new NotSupportedException();
    }

    private sealed class StubBudgetRepository : IBudgetRepository
    {
        public Task<BudgetSummary?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Budget?> GetEntityByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<Budget?>(null);
        public Task<BudgetSummary?> GetByDepartmentAndPeriodAsync(Guid tenantId, Guid departmentId, int month, int year, CancellationToken cancellationToken = default) => Task.FromResult<BudgetSummary?>(null);
        public Task<Budget?> GetEntityByDepartmentAndPeriodAsync(Guid tenantId, Guid departmentId, int month, int year, CancellationToken cancellationToken = default) => Task.FromResult<Budget?>(null);
        public Task<IReadOnlyList<BudgetSummary>> GetByTenantIdAsync(Guid idTenant, int? month, int? year, Guid? departmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(Guid tenantId, Guid departmentId, int month, int year, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<decimal> CalculateSpentAmountAsync(Guid tenantId, Guid departmentId, int month, int year, CancellationToken cancellationToken = default) => Task.FromResult(0m);
        public Task<bool> HasActiveBudgetsForDepartmentAsync(Guid tenantId, Guid departmentId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public void Add(Budget budget) => throw new NotSupportedException();
        public void Update(Budget budget) => throw new NotSupportedException();
    }

    private sealed class StubCategoryRepository : ICategoryRepository
    {
        public Task<CategorySummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CategorySummary>> GetByTenantIdAsync(Guid idTenant, bool includeInactive = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([]);
        public Task<Category?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(Guid idTenant, string name, Guid? excludeId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasExpensesAsync(Guid categoryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(Category category) => throw new NotSupportedException();
        public void Update(Category category) => throw new NotSupportedException();
    }

    private sealed class StubReviewedDocumentRepository(ReviewedDocument document) : IReviewedDocumentRepository
    {
        public Task<IReadOnlyList<ReviewedDocument>> GetByIdsAsync(IReadOnlyList<Guid> ids, Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewedDocument>>(Array.Empty<ReviewedDocument>());
        public void Add(ReviewedDocument document) => throw new NotSupportedException();
        public void Update(ReviewedDocument document) => throw new NotSupportedException();
        public Task<ReviewedDocument?> GetByIdForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(document.Id == id && document.IdTenant == tenantId ? document : null);
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

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public Guid? Id { get; set; }
        public Guid? MembershipId { get; set; }
        public bool IsAvailable => Id.HasValue;
        public bool IsSuperAdmin { get; set; }

        public IDisposable BeginScope(Guid? tenantId, Guid? membershipId = null, bool isSuperAdmin = false)
            => NoOpDisposable.Instance;
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new();
        public void Dispose() { }
    }

    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(1);
        }
    }

    private sealed class NullReimbursementProfileRepository : FinFlow.Domain.Employees.IEmployeeReimbursementProfileRepository
    {
        public Task<FinFlow.Domain.Employees.EmployeeReimbursementProfile?> GetByMembershipIdAsync(Guid membershipId, CancellationToken cancellationToken = default)
        {
            // Return a profile with bank info so BankTransfer payments succeed.
            // Tests for missing-profile behavior live in their own dedicated test class.
            var profile = FinFlow.Domain.Employees.EmployeeReimbursementProfile.Create(Guid.NewGuid(), membershipId).Value;
            profile.UpdateBankInfo("VCB", new byte[32], "1234", "STUB HOLDER", null);
            return Task.FromResult<FinFlow.Domain.Employees.EmployeeReimbursementProfile?>(profile);
        }

        public Task<FinFlow.Domain.Employees.EmployeeReimbursementProfile?> GetByIdForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<FinFlow.Domain.Employees.EmployeeReimbursementProfile?>(null);

        public Task<IReadOnlyList<FinFlow.Domain.Employees.EmployeeReimbursementProfile>> GetByMembershipIdsAsync(IReadOnlyList<Guid> membershipIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FinFlow.Domain.Employees.EmployeeReimbursementProfile>>(Array.Empty<FinFlow.Domain.Employees.EmployeeReimbursementProfile>());
        public void Add(FinFlow.Domain.Employees.EmployeeReimbursementProfile profile) { }
        public void Update(FinFlow.Domain.Employees.EmployeeReimbursementProfile profile) { }
    }

    private sealed class NoOpBudgetReservationService : FinFlow.Application.Budgets.Services.IBudgetReservationService
    {
        public Task<Result> CommitAsync(FinFlow.Application.Budgets.Services.BudgetMovement m, FinFlow.Domain.Enums.BudgetExceededTrigger t, CancellationToken ct) => Task.FromResult(Result.Success());
        public Task<Result> CommitWithOverrideAsync(FinFlow.Application.Budgets.Services.BudgetMovement m, FinFlow.Domain.Enums.BudgetExceededTrigger t, Guid u, string j, decimal o, CancellationToken ct) => Task.FromResult(Result.Success());
        public Task<Result> ReleaseCommitmentAsync(FinFlow.Application.Budgets.Services.BudgetMovement m, CancellationToken ct) => Task.FromResult(Result.Success());
        public Task<Result> ConvertCommitmentToSpentAsync(FinFlow.Application.Budgets.Services.BudgetMovement m, FinFlow.Domain.Enums.BudgetExceededTrigger t, CancellationToken ct) => Task.FromResult(Result.Success());
        public Task<Result> ReverseSpentAsync(FinFlow.Application.Budgets.Services.BudgetMovement m, CancellationToken ct) => Task.FromResult(Result.Success());
        public Task<Result> ReapplySpentAsync(FinFlow.Application.Budgets.Services.BudgetMovement m, FinFlow.Domain.Enums.BudgetExceededTrigger t, CancellationToken ct) => Task.FromResult(Result.Success());
    }
}
