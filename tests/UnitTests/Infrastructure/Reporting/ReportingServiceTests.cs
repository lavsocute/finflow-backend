using FinFlow.Application.Reporting.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using FinFlow.Infrastructure;
using FinFlow.Infrastructure.Reporting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinFlow.UnitTests.Infrastructure.Reporting;

public class ReportingServiceTests
{
    private static readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task GetExpenseSummary_EmptyTenant_ReturnsZero()
    {
        await using var db = CreateDb();
        await SeedTenant(db, "VND");

        var sut = new ReportingService(db);
        var period = ReportingPeriod.Create(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)).Value;
        var summary = await sut.GetExpenseSummaryAsync(_tenantId, period, null, CancellationToken.None);

        Assert.Equal(0, summary.ExpenseCount);
        Assert.Equal(0m, summary.TotalInBaseCurrency);
        Assert.Equal("VND", summary.BaseCurrencyCode);
        Assert.Empty(summary.ByCategory);
    }

    [Fact]
    public async Task GetExpenseSummary_AggregatesBaseCurrency_AcrossDepartments()
    {
        await using var db = CreateDb();
        await SeedTenant(db, "VND");
        var deptA = await SeedDepartment(db, "Marketing");
        var deptB = await SeedDepartment(db, "Engineering");
        var cat = await SeedCategory(db, "Travel");

        AddConfirmedExpense(db, deptA, cat, 1_000_000m, 5, 2026);
        AddConfirmedExpense(db, deptA, cat, 500_000m, 5, 2026);
        AddConfirmedExpense(db, deptB, cat, 2_500_000m, 5, 2026);
        AddConfirmedExpense(db, deptA, cat, 99_999_999m, 4, 2026);  // out of period
        await db.SaveChangesAsync();

        var sut = new ReportingService(db);
        var period = ReportingPeriod.Create(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)).Value;
        var summary = await sut.GetExpenseSummaryAsync(_tenantId, period, null, CancellationToken.None);

        Assert.Equal(3, summary.ExpenseCount);
        Assert.Equal(4_000_000m, summary.TotalInBaseCurrency);
        Assert.Equal(2, summary.ByDepartment.Count);
        Assert.Equal(2_500_000m, summary.ByDepartment.First().AmountInBaseCurrency);
    }

    [Fact]
    public async Task GetExpenseSummary_ScopedToDepartment_ExcludesOthers()
    {
        await using var db = CreateDb();
        await SeedTenant(db, "VND");
        var deptA = await SeedDepartment(db, "Marketing");
        var deptB = await SeedDepartment(db, "Engineering");
        var cat = await SeedCategory(db, "Travel");
        AddConfirmedExpense(db, deptA, cat, 1m, 5, 2026);
        AddConfirmedExpense(db, deptB, cat, 99m, 5, 2026);
        await db.SaveChangesAsync();

        var sut = new ReportingService(db);
        var period = ReportingPeriod.Create(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)).Value;
        var summary = await sut.GetExpenseSummaryAsync(_tenantId, period, deptA, CancellationToken.None);

        Assert.Equal(1, summary.ExpenseCount);
        Assert.Equal(1m, summary.TotalInBaseCurrency);
    }

    [Fact]
    public async Task GetExpenseSummary_MultiCurrency_ExposesByCurrencyBreakdown()
    {
        await using var db = CreateDb();
        await SeedTenant(db, "VND");
        var dept = await SeedDepartment(db, "Engineering");
        var cat = await SeedCategory(db, "Software");
        // USD invoice with 25k VND/USD rate → 100 USD = 2.5M VND
        AddConfirmedExpenseWithCurrency(db, dept, cat, nativeAmount: 100m, baseAmount: 2_500_000m, currency: "USD", 5, 2026);
        AddConfirmedExpense(db, dept, cat, 1_000_000m, 5, 2026);
        await db.SaveChangesAsync();

        var sut = new ReportingService(db);
        var period = ReportingPeriod.Create(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)).Value;
        var summary = await sut.GetExpenseSummaryAsync(_tenantId, period, null, CancellationToken.None);

        Assert.Equal(2, summary.ByCurrency.Count);
        Assert.Equal(2, summary.ExpenseCount);
        Assert.Equal(3_500_000m, summary.TotalInBaseCurrency);
    }

    [Fact]
    public async Task GetExpenseSummary_SingleCurrency_OmitsByCurrencyBreakdown()
    {
        await using var db = CreateDb();
        await SeedTenant(db, "VND");
        var dept = await SeedDepartment(db, "Marketing");
        var cat = await SeedCategory(db, "Travel");
        AddConfirmedExpense(db, dept, cat, 1m, 5, 2026);
        await db.SaveChangesAsync();

        var sut = new ReportingService(db);
        var period = ReportingPeriod.Create(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)).Value;
        var summary = await sut.GetExpenseSummaryAsync(_tenantId, period, null, CancellationToken.None);

        Assert.Empty(summary.ByCurrency);
    }

    [Fact]
    public async Task GetBudgetUtilization_NoBudgets_ReturnsEmpty()
    {
        await using var db = CreateDb();
        await SeedTenant(db, "VND");

        var sut = new ReportingService(db);
        var rows = await sut.GetBudgetUtilizationAsync(_tenantId, 5, 2026, null, CancellationToken.None);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task GetBudgetUtilization_CalculatesPercent_FlagsApproaching()
    {
        await using var db = CreateDb();
        await SeedTenant(db, "VND");
        var dept = await SeedDepartment(db, "Marketing");
        var cat = await SeedCategory(db, "Travel");

        var budget = Budget.Create(_tenantId, dept, 5, 2026, 1_000_000m).Value;
        db.Budgets.Add(budget);
        AddConfirmedExpense(db, dept, cat, 950_000m, 5, 2026);   // 95%
        await db.SaveChangesAsync();

        var sut = new ReportingService(db);
        var rows = await sut.GetBudgetUtilizationAsync(_tenantId, 5, 2026, null, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(950_000m, row.Spent);
        Assert.Equal(50_000m, row.Remaining);
        Assert.Equal(95m, row.UtilizationPercent);
        Assert.True(row.IsApproachingLimit);
        Assert.False(row.IsOverBudget);
    }

    [Fact]
    public async Task GetBudgetUtilization_OverSpending_FlagsOverBudget()
    {
        await using var db = CreateDb();
        await SeedTenant(db, "VND");
        var dept = await SeedDepartment(db, "Sales");
        var cat = await SeedCategory(db, "Travel");

        var budget = Budget.Create(_tenantId, dept, 5, 2026, 1_000_000m).Value;
        db.Budgets.Add(budget);
        AddConfirmedExpense(db, dept, cat, 1_500_000m, 5, 2026);   // 150%
        await db.SaveChangesAsync();

        var sut = new ReportingService(db);
        var rows = await sut.GetBudgetUtilizationAsync(_tenantId, 5, 2026, null, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(150m, row.UtilizationPercent);
        Assert.True(row.IsOverBudget);
        Assert.False(row.IsApproachingLimit);
    }

    [Fact]
    public async Task GetMonthlyTrend_FillsGapsWithZeros()
    {
        await using var db = CreateDb();
        await SeedTenant(db, "VND");
        var dept = await SeedDepartment(db, "Marketing");
        var cat = await SeedCategory(db, "Travel");
        var now = DateTime.UtcNow;
        AddConfirmedExpense(db, dept, cat, 5_000_000m, now.Month, now.Year);
        await db.SaveChangesAsync();

        var sut = new ReportingService(db);
        var points = await sut.GetMonthlyTrendAsync(_tenantId, monthCount: 3, departmentScope: null, CancellationToken.None);

        Assert.Equal(3, points.Count);
        Assert.Contains(points, p => p.ExpenseTotal == 5_000_000m);
        Assert.Contains(points, p => p.ExpenseTotal == 0m);
    }

    // ─── helpers ───
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new StubTenant { Id = _tenantId, MembershipId = Guid.NewGuid() });
    }

    private static async Task SeedTenant(ApplicationDbContext db, string currency)
    {
        var tenant = Tenant.Create("Acme", "ACME-TEST", currency: currency).Value;
        // Force Tenant.Id to match _tenantId so query filter matches.
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(tenant, _tenantId);
        db.Set<Tenant>().Add(tenant);
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedDepartment(ApplicationDbContext db, string name)
    {
        var dept = Department.Create(name, _tenantId).Value;
        db.Set<Department>().Add(dept);
        await db.SaveChangesAsync();
        return dept.Id;
    }

    private static async Task<Guid> SeedCategory(ApplicationDbContext db, string name)
    {
        var cat = Category.CreateUserDefined(_tenantId, name, null, "icon", "#000", 0).Value;
        db.Categories.Add(cat);
        await db.SaveChangesAsync();
        return cat.Id;
    }

    private static void AddConfirmedExpense(
        ApplicationDbContext db,
        Guid deptId,
        Guid catId,
        decimal vndAmount,
        int month,
        int year)
        => AddConfirmedExpenseWithCurrency(db, deptId, catId, vndAmount, vndAmount, "VND", month, year);

    private static void AddConfirmedExpenseWithCurrency(
        ApplicationDbContext db,
        Guid deptId,
        Guid catId,
        decimal nativeAmount,
        decimal baseAmount,
        string currency,
        int month,
        int year)
    {
        var docId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var expense = Expense.Create(
            idTenant: _tenantId,
            idDepartment: deptId,
            documentId: docId,
            paymentId: paymentId,
            idCategory: catId,
            vendorName: "test-vendor",
            amount: nativeAmount,
            currencyCode: currency,
            amountInBaseCurrency: baseAmount,
            baseCurrencyCode: "VND",
            month: month,
            year: year,
            expenseDate: new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc),
            createdByMembershipId: Guid.NewGuid()).Value;
        db.Expenses.Add(expense);
    }

    private sealed class StubTenant : ICurrentTenant
    {
        public Guid? Id { get; set; }
        public Guid? MembershipId { get; set; }
        public bool IsSuperAdmin => false;
        public bool IsAvailable => Id.HasValue;

        public IDisposable BeginScope(Guid? tenantId, Guid? membershipId = null, bool isSuperAdmin = false)
            => Disposable.Instance;
        private sealed class Disposable : IDisposable
        {
            public static readonly Disposable Instance = new();
            public void Dispose() { }
        }
    }
}
