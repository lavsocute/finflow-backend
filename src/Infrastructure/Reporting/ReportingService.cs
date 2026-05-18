using FinFlow.Application.Reporting;
using FinFlow.Application.Reporting.DTOs;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Expenses;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Reporting;

/// <summary>
/// SQL aggregation queries powering the reporting dashboard. All methods:
///  - AsNoTracking
///  - Tenant-scoped via the entity's <c>IMultiTenant</c> query filter
///  - Return base-currency normalized amounts so cross-currency comparison works
/// </summary>
internal sealed class ReportingService : IReportingService
{
    private readonly ApplicationDbContext _dbContext;

    public ReportingService(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<ExpenseSummaryDto> GetExpenseSummaryAsync(
        Guid tenantId,
        ReportingPeriod period,
        Guid? departmentScope,
        CancellationToken cancellationToken = default)
    {
        var fromMonth = period.From.Year * 12 + period.From.Month;
        var toMonth = period.To.Year * 12 + period.To.Month;

        var baseCurrency = await GetBaseCurrencyAsync(tenantId, cancellationToken);

        // Base query: confirmed expenses in the period for the active tenant.
        var query = _dbContext.Expenses
            .AsNoTracking()
            .Where(e => e.IdTenant == tenantId
                && e.Status == ExpenseStatus.Confirmed
                && (e.Year * 12 + e.Month) >= fromMonth
                && (e.Year * 12 + e.Month) <= toMonth);

        if (departmentScope.HasValue)
            query = query.Where(e => e.IdDepartment == departmentScope.Value);

        var topAggregate = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                TotalBase = g.Sum(e => e.AmountInBaseCurrency)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var byCategoryRaw = await query
            .GroupBy(e => e.IdCategory)
            .Select(g => new
            {
                CategoryId = g.Key,
                Total = g.Sum(e => e.AmountInBaseCurrency),
                Count = g.Count()
            })
            .ToListAsync(cancellationToken);

        var categoryNames = await _dbContext.Set<Category>()
            .AsNoTracking()
            .Where(c => byCategoryRaw.Select(x => x.CategoryId).Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(cancellationToken);
        var categoryNameMap = categoryNames.ToDictionary(c => c.Id, c => c.Name);

        var byCategory = byCategoryRaw
            .Select(x => new ExpenseSummaryGroupDto(
                x.CategoryId,
                categoryNameMap.GetValueOrDefault(x.CategoryId, "(unknown)"),
                x.Total,
                x.Count))
            .OrderByDescending(g => g.AmountInBaseCurrency)
            .ToList();

        var byDeptRaw = await query
            .GroupBy(e => e.IdDepartment)
            .Select(g => new
            {
                DepartmentId = g.Key,
                Total = g.Sum(e => e.AmountInBaseCurrency),
                Count = g.Count()
            })
            .ToListAsync(cancellationToken);

        var deptNames = await _dbContext.Set<Department>()
            .AsNoTracking()
            .Where(d => byDeptRaw.Select(x => x.DepartmentId).Contains(d.Id))
            .Select(d => new { d.Id, d.Name })
            .ToListAsync(cancellationToken);
        var deptNameMap = deptNames.ToDictionary(d => d.Id, d => d.Name);

        var byDepartment = byDeptRaw
            .Select(x => new ExpenseSummaryGroupDto(
                x.DepartmentId,
                deptNameMap.GetValueOrDefault(x.DepartmentId, "(unknown)"),
                x.Total,
                x.Count))
            .OrderByDescending(g => g.AmountInBaseCurrency)
            .ToList();

        var byCurrencyRaw = await query
            .GroupBy(e => e.CurrencyCode)
            .Select(g => new ExpenseSummaryByCurrencyDto(
                g.Key,
                g.Sum(e => e.Amount),
                g.Sum(e => e.AmountInBaseCurrency),
                g.Count()))
            .ToListAsync(cancellationToken);

        // Only expose ByCurrency when more than one currency is in play —
        // otherwise it's redundant with the top-level totals.
        IReadOnlyList<ExpenseSummaryByCurrencyDto> byCurrency = byCurrencyRaw.Count > 1
            ? byCurrencyRaw.OrderByDescending(c => c.AmountInBaseCurrency).ToList()
            : [];

        return new ExpenseSummaryDto(
            ExpenseCount: topAggregate?.Count ?? 0,
            TotalInBaseCurrency: topAggregate?.TotalBase ?? 0m,
            BaseCurrencyCode: baseCurrency,
            ByCategory: byCategory,
            ByDepartment: byDepartment,
            ByCurrency: byCurrency);
    }

    public async Task<IReadOnlyList<BudgetUtilizationDto>> GetBudgetUtilizationAsync(
        Guid tenantId,
        int month,
        int year,
        Guid? departmentId,
        CancellationToken cancellationToken = default)
    {
        var baseCurrency = await GetBaseCurrencyAsync(tenantId, cancellationToken);

        var budgetQuery = _dbContext.Budgets
            .AsNoTracking()
            .Where(b => b.IdTenant == tenantId
                && b.Month == month
                && b.Year == year);

        if (departmentId.HasValue)
            budgetQuery = budgetQuery.Where(b => b.IdDepartment == departmentId.Value);

        var budgets = await budgetQuery
            .Select(b => new
            {
                b.Id,
                b.IdDepartment,
                b.AllocatedAmount
            })
            .ToListAsync(cancellationToken);

        if (budgets.Count == 0)
            return [];

        var deptIds = budgets.Select(b => b.IdDepartment).Distinct().ToList();

        var deptNames = await _dbContext.Set<Department>()
            .AsNoTracking()
            .Where(d => deptIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Name })
            .ToListAsync(cancellationToken);
        var deptNameMap = deptNames.ToDictionary(d => d.Id, d => d.Name);

        var spentByDept = await _dbContext.Expenses
            .AsNoTracking()
            .Where(e => e.IdTenant == tenantId
                && e.Status == ExpenseStatus.Confirmed
                && e.Month == month
                && e.Year == year
                && deptIds.Contains(e.IdDepartment))
            .GroupBy(e => e.IdDepartment)
            .Select(g => new { DepartmentId = g.Key, Total = g.Sum(e => e.AmountInBaseCurrency) })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Total, cancellationToken);

        return budgets
            .Select(b =>
            {
                var spent = spentByDept.GetValueOrDefault(b.IdDepartment, 0m);
                var remaining = b.AllocatedAmount - spent;
                var pct = b.AllocatedAmount > 0
                    ? Math.Round((spent / b.AllocatedAmount) * 100m, 2, MidpointRounding.AwayFromZero)
                    : 0m;
                return new BudgetUtilizationDto(
                    DepartmentId: b.IdDepartment,
                    DepartmentName: deptNameMap.GetValueOrDefault(b.IdDepartment, "(unknown)"),
                    Month: month,
                    Year: year,
                    Allocated: b.AllocatedAmount,
                    Spent: spent,
                    Remaining: remaining,
                    UtilizationPercent: pct,
                    IsApproachingLimit: pct >= 90m && pct < 100m,
                    IsOverBudget: pct >= 100m,
                    BaseCurrencyCode: baseCurrency);
            })
            .OrderByDescending(d => d.UtilizationPercent)
            .ToList();
    }

    public async Task<IReadOnlyList<TopVendorDto>> GetTopVendorsAsync(
        Guid tenantId,
        ReportingPeriod period,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var baseCurrency = await GetBaseCurrencyAsync(tenantId, cancellationToken);
        var fromDate = period.From;
        var toDate = period.To;

        // Aggregate from ReviewedDocument (document-level). Confirmed expenses
        // alone would miss documents not yet reimbursed.
        var raw = await _dbContext.ReviewedDocuments
            .AsNoTracking()
            .Where(d => d.IdTenant == tenantId
                && d.Status == ReviewedDocumentStatus.Approved
                && d.IdVendor != null
                && d.DocumentDate >= fromDate
                && d.DocumentDate <= toDate)
            .GroupBy(d => d.IdVendor!.Value)
            .Select(g => new
            {
                VendorId = g.Key,
                DocumentCount = g.Count(),
                Total = g.Sum(d => d.TotalAmount * d.ExchangeRate)
            })
            .OrderByDescending(x => x.Total)
            .Take(limit)
            .ToListAsync(cancellationToken);

        if (raw.Count == 0)
            return [];

        var vendorIds = raw.Select(r => r.VendorId).ToList();

        var vendors = await _dbContext.Set<Vendor>()
            .AsNoTracking()
            .Where(v => vendorIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Name, v.TaxCode, v.IsVerified })
            .ToListAsync(cancellationToken);
        var vendorMap = vendors.ToDictionary(v => v.Id);

        return raw
            .Select(r =>
            {
                vendorMap.TryGetValue(r.VendorId, out var v);
                return new TopVendorDto(
                    VendorId: r.VendorId,
                    VendorName: v?.Name ?? "(unknown)",
                    TaxCode: v?.TaxCode ?? string.Empty,
                    IsVerified: v?.IsVerified ?? false,
                    DocumentCount: r.DocumentCount,
                    TotalAmountInBaseCurrency: decimal.Round(r.Total, 2, MidpointRounding.AwayFromZero),
                    BaseCurrencyCode: baseCurrency);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<TopEmployeeDto>> GetTopEmployeesAsync(
        Guid tenantId,
        ReportingPeriod period,
        Guid? departmentScope,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var baseCurrency = await GetBaseCurrencyAsync(tenantId, cancellationToken);
        var fromMonth = period.From.Year * 12 + period.From.Month;
        var toMonth = period.To.Year * 12 + period.To.Month;

        var query = _dbContext.Expenses
            .AsNoTracking()
            .Where(e => e.IdTenant == tenantId
                && e.Status == ExpenseStatus.Confirmed
                && (e.Year * 12 + e.Month) >= fromMonth
                && (e.Year * 12 + e.Month) <= toMonth);

        if (departmentScope.HasValue)
            query = query.Where(e => e.IdDepartment == departmentScope.Value);

        // Expense.CreatedByMembershipId is the membership of the staff who
        // submitted (it carries through ReviewedDocument.MembershipId).
        var raw = await query
            .GroupBy(e => e.CreatedByMembershipId)
            .Select(g => new
            {
                MembershipId = g.Key,
                Total = g.Sum(e => e.AmountInBaseCurrency),
                Count = g.Count()
            })
            .OrderByDescending(x => x.Total)
            .Take(limit)
            .ToListAsync(cancellationToken);

        if (raw.Count == 0)
            return [];

        var membershipIds = raw.Select(r => r.MembershipId).ToList();

        // Join: Membership -> Account (FullName) + Membership.DepartmentId -> Department
        var memberships = await _dbContext.Set<TenantMembership>()
            .AsNoTracking()
            .Where(m => membershipIds.Contains(m.Id))
            .Select(m => new { m.Id, m.AccountId, m.DepartmentId })
            .ToListAsync(cancellationToken);
        var membershipMap = memberships.ToDictionary(m => m.Id);

        var accountIds = memberships.Select(m => m.AccountId).Distinct().ToList();
        var deptIds = memberships
            .Where(m => m.DepartmentId.HasValue)
            .Select(m => m.DepartmentId!.Value)
            .Distinct()
            .ToList();

        var accountNames = await _dbContext.Set<Account>()
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.FullName, a.Email })
            .ToListAsync(cancellationToken);
        var accountMap = accountNames.ToDictionary(a => a.Id);

        var deptNames = await _dbContext.Set<Department>()
            .AsNoTracking()
            .Where(d => deptIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Name })
            .ToListAsync(cancellationToken);
        var deptNameMap = deptNames.ToDictionary(d => d.Id, d => d.Name);

        return raw
            .Select(r =>
            {
                membershipMap.TryGetValue(r.MembershipId, out var m);
                var account = m is null ? null : accountMap.GetValueOrDefault(m.AccountId);
                var deptName = m?.DepartmentId.HasValue == true
                    ? deptNameMap.GetValueOrDefault(m.DepartmentId.Value, "(unknown)")
                    : "(no dept)";
                return new TopEmployeeDto(
                    MembershipId: r.MembershipId,
                    AccountId: m?.AccountId ?? Guid.Empty,
                    EmployeeName: account?.FullName ?? account?.Email ?? "(unknown)",
                    DepartmentName: deptName,
                    ExpenseCount: r.Count,
                    TotalAmountInBaseCurrency: r.Total,
                    BaseCurrencyCode: baseCurrency);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<PendingPaymentItemDto>> GetPendingPaymentQueueAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var baseCurrency = await GetBaseCurrencyAsync(tenantId, cancellationToken);
        var now = DateTime.UtcNow;

        // Pull payments + ancillary in parallel-friendly steps.
        var payments = await _dbContext.Payments
            .AsNoTracking()
            .Where(p => p.IdTenant == tenantId && p.Status == PaymentStatus.Pending)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new
            {
                p.Id,
                p.DocumentId,
                p.IdDepartment,
                p.Amount,
                p.CurrencyCode,
                p.AmountInBaseCurrency,
                p.Method,
                p.RecordedAt
            })
            .ToListAsync(cancellationToken);

        if (payments.Count == 0)
            return [];

        var docIds = payments.Select(p => p.DocumentId).ToList();
        var deptIds = payments.Select(p => p.IdDepartment).Distinct().ToList();

        var docs = await _dbContext.ReviewedDocuments
            .AsNoTracking()
            .Where(d => docIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Reference, d.MembershipId })
            .ToListAsync(cancellationToken);
        var docMap = docs.ToDictionary(d => d.Id);

        var membershipIds = docs.Select(d => d.MembershipId).Distinct().ToList();
        var memberships = await _dbContext.Set<TenantMembership>()
            .AsNoTracking()
            .Where(m => membershipIds.Contains(m.Id))
            .Select(m => new { m.Id, m.AccountId })
            .ToListAsync(cancellationToken);
        var membershipMap = memberships.ToDictionary(m => m.Id);

        var accountIds = memberships.Select(m => m.AccountId).Distinct().ToList();
        var accounts = await _dbContext.Set<Account>()
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.FullName, a.Email })
            .ToListAsync(cancellationToken);
        var accountMap = accounts.ToDictionary(a => a.Id);

        var deptNames = await _dbContext.Set<Department>()
            .AsNoTracking()
            .Where(d => deptIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Name })
            .ToListAsync(cancellationToken);
        var deptNameMap = deptNames.ToDictionary(d => d.Id, d => d.Name);

        return payments
            .Select(p =>
            {
                var doc = docMap.GetValueOrDefault(p.DocumentId);
                var membership = doc is null ? null : membershipMap.GetValueOrDefault(doc.MembershipId);
                var account = membership is null ? null : accountMap.GetValueOrDefault(membership.AccountId);
                return new PendingPaymentItemDto(
                    PaymentId: p.Id,
                    DocumentId: p.DocumentId,
                    Reference: doc?.Reference ?? string.Empty,
                    EmployeeName: account?.FullName ?? account?.Email ?? "(unknown)",
                    DepartmentName: deptNameMap.GetValueOrDefault(p.IdDepartment, "(unknown)"),
                    Amount: p.Amount,
                    CurrencyCode: p.CurrencyCode,
                    AmountInBaseCurrency: p.AmountInBaseCurrency,
                    BaseCurrencyCode: baseCurrency,
                    PaymentMethod: p.Method.ToString(),
                    RecordedAt: p.RecordedAt,
                    AgeDays: Math.Max(0, (int)(now - p.RecordedAt).TotalDays));
            })
            .ToList();
    }

    public async Task<IReadOnlyList<MonthlyTrendPointDto>> GetMonthlyTrendAsync(
        Guid tenantId,
        int monthCount,
        Guid? departmentScope,
        CancellationToken cancellationToken = default)
    {
        var baseCurrency = await GetBaseCurrencyAsync(tenantId, cancellationToken);
        var period = ReportingPeriod.LastNMonths(monthCount);
        var fromMonth = period.From.Year * 12 + period.From.Month;
        var toMonth = period.To.Year * 12 + period.To.Month;

        var query = _dbContext.Expenses
            .AsNoTracking()
            .Where(e => e.IdTenant == tenantId
                && e.Status == ExpenseStatus.Confirmed
                && (e.Year * 12 + e.Month) >= fromMonth
                && (e.Year * 12 + e.Month) <= toMonth);

        if (departmentScope.HasValue)
            query = query.Where(e => e.IdDepartment == departmentScope.Value);

        var aggregates = await query
            .GroupBy(e => new { e.Year, e.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Total = g.Sum(e => e.AmountInBaseCurrency),
                DocCount = g.Select(e => e.DocumentId).Distinct().Count()
            })
            .ToListAsync(cancellationToken);

        // Fill gaps so the chart has a continuous X-axis.
        var byKey = aggregates.ToDictionary(a => (a.Year, a.Month));
        var result = new List<MonthlyTrendPointDto>(monthCount);
        var cursor = period.From;
        while (cursor <= period.To)
        {
            byKey.TryGetValue((cursor.Year, cursor.Month), out var hit);
            result.Add(new MonthlyTrendPointDto(
                Year: cursor.Year,
                Month: cursor.Month,
                ExpenseTotal: hit?.Total ?? 0m,
                DocumentCount: hit?.DocCount ?? 0,
                BaseCurrencyCode: baseCurrency));
            cursor = cursor.AddMonths(1);
        }
        return result;
    }

    private async Task<string> GetBaseCurrencyAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var currency = await _dbContext.Set<Tenant>()
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Currency)
            .FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(currency) ? "VND" : currency;
    }
}
