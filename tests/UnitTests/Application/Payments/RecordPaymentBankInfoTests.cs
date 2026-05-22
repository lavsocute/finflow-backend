using FinFlow.Application.Payments.Commands.RecordPayment;
using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Employees;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using Xunit;

namespace FinFlow.UnitTests.Application.Payments;

/// <summary>
/// Behavior under multi-currency reimbursement profile rules:
///  - BankTransfer requires the employee to have configured an active bank profile.
///  - Cash, Payroll, Other do not require it (legacy / on-prem payroll allowed).
/// </summary>
public class RecordPaymentBankInfoTests
{
    [Fact]
    public async Task BankTransfer_WhenEmployeeHasNoProfile_ReturnsBankInfoMissing()
    {
        var fixture = TestFixture.Build(profileForEmployee: null);

        var result = await fixture.Handler.Handle(
            new RecordPaymentCommand(fixture.Document.Id, "BankTransfer", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrors.BankInfoMissing, result.Error);
    }

    [Fact]
    public async Task BankTransfer_WhenProfileExistsWithoutBankFields_ReturnsBankInfoMissing()
    {
        var emptyProfile = EmployeeReimbursementProfile.Create(Guid.NewGuid(), Guid.NewGuid()).Value;
        // No bank info set — profile exists but HasBankInfo == false.
        var fixture = TestFixture.Build(profileForEmployee: emptyProfile);

        var result = await fixture.Handler.Handle(
            new RecordPaymentCommand(fixture.Document.Id, "BankTransfer", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrors.BankInfoMissing, result.Error);
    }

    [Fact]
    public async Task BankTransfer_WhenProfileHasBankInfo_Succeeds()
    {
        var profile = EmployeeReimbursementProfile.Create(Guid.NewGuid(), Guid.NewGuid()).Value;
        profile.UpdateBankInfo("VCB", new byte[32], "1234", "TEST USER", null);
        var fixture = TestFixture.Build(profileForEmployee: profile);

        var result = await fixture.Handler.Handle(
            new RecordPaymentCommand(fixture.Document.Id, "BankTransfer", null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("Cash")]
    [InlineData("Payroll")]
    [InlineData("Other")]
    public async Task NonBankMethods_WhenProfileMissing_StillSucceed(string method)
    {
        var fixture = TestFixture.Build(profileForEmployee: null);

        var result = await fixture.Handler.Handle(
            new RecordPaymentCommand(fixture.Document.Id, method, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private sealed class TestFixture
    {
        public required RecordPaymentCommandHandler Handler { get; init; }
        public required ReviewedDocument Document { get; init; }

        public static TestFixture Build(EmployeeReimbursementProfile? profileForEmployee)
        {
            var tenantId = Guid.NewGuid();
            var membershipId = Guid.NewGuid();
            var deptId = Guid.NewGuid();

            var doc = ReviewedDocument.CreateSubmitted(
                Guid.NewGuid(),
                tenantId,
                deptId,
                membershipId,
                "receipt.pdf",
                "application/pdf",
                "Acme",
                "INV-1",
                new DateOnly(2026, 5, 1),
                "Travel",
                null,
                100m,
                0m,
                100m,
                "staff-upload",
                "staff@finflow.test",
                "Staff corrected",
                DateTime.UtcNow,
                [ReviewedDocumentLineItem.Create("Item", 1m, 100m, 100m)]).Value;
            doc.Approve();

            var handler = new RecordPaymentCommandHandler(
                new StubPaymentRepository(),
                new StubExpenseRepository(),
                new StubReviewedDocumentRepository(doc),
                new StubBudgetRepository(),
                new StubCategoryRepository(),
                new StubProfileRepo(profileForEmployee),
                new StubCurrentTenant { Id = tenantId, MembershipId = Guid.NewGuid() },
                new StubUnitOfWork());

            return new TestFixture { Handler = handler, Document = doc };
        }
    }

    private sealed class StubPaymentRepository : IPaymentRepository
    {
        public Task<PaymentSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<PaymentSummary?>(null);
        public Task<PaymentSummary?> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default) => Task.FromResult<PaymentSummary?>(null);
        public Task<IReadOnlyList<PaymentSummary>> GetByTenantIdAsync(Guid idTenant, PaymentStatus? status = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PaymentSummary>>(Array.Empty<PaymentSummary>());
        public Task<IReadOnlyList<PaymentSummary>> GetPendingByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PaymentSummary>>(Array.Empty<PaymentSummary>());
        public Task<bool> ExistsByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<Payment?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Payment?>(null);
        public Task<IReadOnlyList<Payment>> GetByIdsAsync(IReadOnlyList<Guid> ids, Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Payment>>(Array.Empty<Payment>());
        public void Add(Payment payment) { }
        public void Update(Payment payment) { }
    }

    private sealed class StubExpenseRepository : IExpenseRepository
    {
        public Task<ExpenseSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<ExpenseSummary?>(null);
        public Task<ExpenseSummary?> GetByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default) => Task.FromResult<ExpenseSummary?>(null);
        public Task<IReadOnlyList<ExpenseSummary>> GetByDepartmentAndPeriodAsync(Guid departmentId, int month, int year, ExpenseStatus? status = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ExpenseSummary>>(Array.Empty<ExpenseSummary>());
        public Task<decimal> GetTotalSpentByDepartmentAndPeriodAsync(Guid departmentId, int month, int year, ExpenseStatus status, CancellationToken cancellationToken = default) => Task.FromResult(0m);
        public Task<Expense?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Expense?>(null);
        public void Add(Expense expense) { }
        public void Update(Expense expense) { }
    }

    private sealed class StubReviewedDocumentRepository : IReviewedDocumentRepository
    {
        private readonly ReviewedDocument _document;
        public StubReviewedDocumentRepository(ReviewedDocument document) => _document = document;
        public Task<ReviewedDocument?> GetByIdForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReviewedDocument?>(_document.Id == id && _document.IdTenant == tenantId ? _document : null);
        public Task<ReviewedDocument?> GetOwnedByIdAsync(Guid id, Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) => Task.FromResult<ReviewedDocument?>(null);
        public Task<bool> ExistsAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<ReviewedDocument>> GetAllActiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewedDocument>>(Array.Empty<ReviewedDocument>());
        public Task<IReadOnlyList<ReviewedDocument>> GetReadyForApprovalAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewedDocument>>(Array.Empty<ReviewedDocument>());
        public Task<IReadOnlyList<ReviewedDocument>> GetReadyForApprovalByDepartmentAsync(Guid tenantId, Guid departmentId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewedDocument>>(Array.Empty<ReviewedDocument>());
        public Task<IReadOnlyList<ReviewedDocument>> GetOwnedReadyForApprovalAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewedDocument>>(Array.Empty<ReviewedDocument>());
        public Task<IReadOnlyList<ReviewedDocument>> GetOwnedSubmittedAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewedDocument>>(Array.Empty<ReviewedDocument>());
        public Task<IReadOnlyList<ReviewedDocument>> GetByStatusAsync(Guid tenantId, ApprovalStatusFilter status, string? search, int page, int pageSize, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewedDocument>>(Array.Empty<ReviewedDocument>());
        public Task<int> CountByStatusAsync(Guid tenantId, ApprovalStatusFilter status, string? search, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<IReadOnlyList<ReviewedDocument>> GetByIdsAsync(IReadOnlyList<Guid> ids, Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewedDocument>>(Array.Empty<ReviewedDocument>());
        public void Add(ReviewedDocument document) { }
        public void Update(ReviewedDocument document) { }
    }

    private sealed class StubBudgetRepository : IBudgetRepository
    {
        public Task<BudgetSummary?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<BudgetSummary?>(null);
        public Task<Budget?> GetEntityByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<Budget?>(null);
        public Task<BudgetSummary?> GetByDepartmentAndPeriodAsync(Guid tenantId, Guid departmentId, int month, int year, CancellationToken cancellationToken = default) => Task.FromResult<BudgetSummary?>(null);
        public Task<Budget?> GetEntityByDepartmentAndPeriodAsync(Guid tenantId, Guid departmentId, int month, int year, CancellationToken cancellationToken = default) => Task.FromResult<Budget?>(null);
        public Task<IReadOnlyList<BudgetSummary>> GetByTenantIdAsync(Guid idTenant, int? month, int? year, Guid? departmentId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BudgetSummary>>(Array.Empty<BudgetSummary>());
        public Task<bool> ExistsAsync(Guid tenantId, Guid departmentId, int month, int year, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<decimal> CalculateSpentAmountAsync(Guid tenantId, Guid departmentId, int month, int year, CancellationToken cancellationToken = default) => Task.FromResult(0m);
        public Task<bool> HasActiveBudgetsForDepartmentAsync(Guid tenantId, Guid departmentId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public void Add(Budget budget) { }
        public void Update(Budget budget) { }
    }

    private sealed class StubCategoryRepository : ICategoryRepository
    {
        public Task<CategorySummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<CategorySummary?>(null);
        public Task<IReadOnlyList<CategorySummary>> GetByTenantIdAsync(Guid idTenant, bool includeInactive = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>(Array.Empty<CategorySummary>());
        public Task<Category?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Category?>(null);
        public Task<bool> ExistsAsync(Guid idTenant, string name, Guid? excludeId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> HasExpensesAsync(Guid categoryId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public void Add(Category category) { }
        public void Update(Category category) { }
    }

    private sealed class StubProfileRepo : IEmployeeReimbursementProfileRepository
    {
        private readonly EmployeeReimbursementProfile? _profile;
        public StubProfileRepo(EmployeeReimbursementProfile? profile) => _profile = profile;

        public Task<EmployeeReimbursementProfile?> GetByMembershipIdAsync(Guid membershipId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_profile);

        public Task<EmployeeReimbursementProfile?> GetByIdForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_profile);

        public Task<IReadOnlyList<EmployeeReimbursementProfile>> GetByMembershipIdsAsync(IReadOnlyList<Guid> membershipIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EmployeeReimbursementProfile>>(Array.Empty<EmployeeReimbursementProfile>());
        public void Add(EmployeeReimbursementProfile profile) { }
        public void Update(EmployeeReimbursementProfile profile) { }
    }

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public Guid? Id { get; set; }
        public Guid? MembershipId { get; set; }
        public bool IsAvailable => Id.HasValue;
        public bool IsSuperAdmin { get; set; }
        public IDisposable BeginScope(Guid? tenantId, Guid? membershipId = null, bool isSuperAdmin = false) =>
            new NoOpDisposable();
        private sealed class NoOpDisposable : IDisposable { public void Dispose() { } }
    }

    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }
}
