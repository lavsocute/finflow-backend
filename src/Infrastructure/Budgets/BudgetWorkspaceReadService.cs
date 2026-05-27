using FinFlow.Application.Budgets.Services;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Budgets;

internal sealed class BudgetWorkspaceReadService : IBudgetWorkspaceReadService
{
    private const int ActivityLimit = 6;
    private const int TrendWindowMonths = 6;
    private readonly ApplicationDbContext _dbContext;

    public BudgetWorkspaceReadService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BudgetWorkspaceReadModel> GetWorkspaceAsync(
        Guid tenantId,
        Guid requesterMembershipId,
        RoleType requesterRole,
        int month,
        int year,
        Guid? selectedBudgetId,
        CancellationToken cancellationToken = default)
    {
        var departments = await _dbContext.Set<Department>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(department => department.IdTenant == tenantId)
            .Select(department => new DepartmentRow(
                department.Id,
                department.Name,
                department.ParentId,
                department.CreatedAt))
            .ToListAsync(cancellationToken);

        var memberships = await (
            from membership in _dbContext.Set<TenantMembership>().AsNoTracking().IgnoreQueryFilters()
            join account in _dbContext.Set<Account>().AsNoTracking().IgnoreQueryFilters()
                on membership.AccountId equals account.Id
            where membership.IdTenant == tenantId
            select new MembershipRow(
                membership.Id,
                membership.DepartmentId,
                membership.Role,
                membership.IsActive,
                account.Email,
                account.FullName))
            .ToListAsync(cancellationToken);

        var scopedDepartmentId = requesterRole == RoleType.Manager
            ? memberships.FirstOrDefault(membership => membership.Id == requesterMembershipId)?.DepartmentId
            : null;

        var periodBudgets = await _dbContext.Budgets
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(budget => budget.IdTenant == tenantId)
            .Where(budget => budget.Month == month && budget.Year == year)
            .Where(budget => !scopedDepartmentId.HasValue || budget.IdDepartment == scopedDepartmentId.Value)
            .OrderByDescending(budget => budget.IsActive)
            .ThenBy(budget => budget.IdDepartment)
            .Select(budget => new BudgetRow(
                budget.Id,
                budget.IdDepartment,
                budget.Month,
                budget.Year,
                budget.AllocatedAmount,
                budget.CarryOverFromPreviousMonth ?? 0m,
                budget.CommittedAmount,
                budget.SpentAmount,
                budget.BaseCurrencyCode,
                budget.EnforcementMode,
                budget.IsActive,
                budget.CreatedAt,
                budget.UpdatedAt))
            .ToListAsync(cancellationToken);

        var trendStart = new DateOnly(year, month, 1).AddMonths(-(TrendWindowMonths - 1));
        var allBudgets = await _dbContext.Budgets
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(budget => budget.IdTenant == tenantId)
            .Where(budget => !scopedDepartmentId.HasValue || budget.IdDepartment == scopedDepartmentId.Value)
            .Select(budget => new BudgetRow(
                budget.Id,
                budget.IdDepartment,
                budget.Month,
                budget.Year,
                budget.AllocatedAmount,
                budget.CarryOverFromPreviousMonth ?? 0m,
                budget.CommittedAmount,
                budget.SpentAmount,
                budget.BaseCurrencyCode,
                budget.EnforcementMode,
                budget.IsActive,
                budget.CreatedAt,
                budget.UpdatedAt))
            .ToListAsync(cancellationToken);
        allBudgets = allBudgets
            .Where(budget => new DateOnly(budget.Year, budget.Month, 1) >= trendStart)
            .ToList();

        var documents = await (
            from document in _dbContext.ReviewedDocuments.AsNoTracking().IgnoreQueryFilters()
            join membership in _dbContext.Set<TenantMembership>().AsNoTracking().IgnoreQueryFilters()
                on document.MembershipId equals membership.Id into membershipJoin
            from membership in membershipJoin.DefaultIfEmpty()
            join account in _dbContext.Set<Account>().AsNoTracking().IgnoreQueryFilters()
                on membership.AccountId equals account.Id into accountJoin
            from account in accountJoin.DefaultIfEmpty()
            where document.IdTenant == tenantId
                && document.DocumentDate.Month == month
                && document.DocumentDate.Year == year
                && (!scopedDepartmentId.HasValue || document.IdDepartment == scopedDepartmentId.Value)
            orderby document.SubmittedAt descending
            select new DocumentRow(
                document.Id,
                document.IdDepartment,
                document.Reference,
                account == null ? document.ReviewedByStaff : (account.FullName ?? account.Email),
                document.TotalAmountInBaseCurrency,
                document.Status,
                document.SubmittedAt))
            .ToListAsync(cancellationToken);

        var documentLookup = documents
            .GroupBy(document => document.DepartmentId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var trendLookup = allBudgets
            .GroupBy(budget => budget.DepartmentId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var setByName = ResolveSetByName(memberships);
        var budgetCards = periodBudgets
            .Select(budget => BuildBudgetCard(
                budget,
                departments,
                documentLookup.GetValueOrDefault(budget.DepartmentId) ?? [],
                trendLookup.GetValueOrDefault(budget.DepartmentId) ?? [],
                setByName))
            .ToList();

        var selected = budgetCards.FirstOrDefault(budget => budget.Id == selectedBudgetId)
            ?? budgetCards.FirstOrDefault(budget => budget.IsActive)
            ?? budgetCards.FirstOrDefault();

        var activeBudgets = budgetCards.Where(budget => budget.IsActive).ToList();
        var totalAllocated = activeBudgets.Sum(budget => budget.AllocatedAmount + budget.CarryOverAmount);
        var totalCommitted = activeBudgets.Sum(budget => budget.CommittedAmount);
        var totalSpent = activeBudgets.Sum(budget => budget.SpentAmount);
        var availablePool = totalAllocated - totalCommitted - totalSpent;
        var committedDocumentCount = activeBudgets.Sum(budget => budget.CommittedDocumentCount);
        var paidDocumentCount = activeBudgets.Sum(budget => budget.PaidDocumentCount);
        var overBudgetCount = activeBudgets.Count(budget => budget.AvailableAmount < 0m);

        return new BudgetWorkspaceReadModel(
            new BudgetWorkspaceSummaryReadModel(
                PeriodLabel(month, year),
                totalAllocated,
                totalCommitted,
                totalSpent,
                availablePool,
                activeBudgets.Count,
                overBudgetCount,
                committedDocumentCount,
                paidDocumentCount,
                overBudgetCount == 0,
                budgetCards.FirstOrDefault()?.CurrencyCode ?? "VND",
                scopedDepartmentId.HasValue
                    ? departments.FirstOrDefault(department => department.Id == scopedDepartmentId.Value)?.Name
                    : null),
            budgetCards,
            selected);
    }

    private static BudgetWorkspaceBudgetReadModel BuildBudgetCard(
        BudgetRow budget,
        IReadOnlyList<DepartmentRow> departments,
        IReadOnlyList<DocumentRow> documents,
        IReadOnlyList<BudgetRow> trendRows,
        string setByName)
    {
        var pool = budget.AllocatedAmount + budget.CarryOverAmount;
        var available = pool - budget.CommittedAmount - budget.SpentAmount;
        var utilization = pool <= 0m
            ? 0m
            : Math.Round((budget.CommittedAmount + budget.SpentAmount) / pool * 100m, 2, MidpointRounding.AwayFromZero);
        var status = ResolveStatus(budget.IsActive, available, utilization);

        return new BudgetWorkspaceBudgetReadModel(
            budget.Id,
            budget.DepartmentId,
            departments.FirstOrDefault(department => department.Id == budget.DepartmentId)?.Name
                ?? "Phòng ban chưa đặt tên",
            DepartmentPath(budget.DepartmentId, departments),
            PeriodLabel(budget.Month, budget.Year),
            budget.AllocatedAmount,
            budget.CarryOverAmount,
            budget.CommittedAmount,
            budget.SpentAmount,
            available,
            utilization,
            budget.EnforcementMode.ToString(),
            status,
            budget.IsActive,
            budget.UpdatedAt,
            documents.Count,
            documents.Count(document => document.Status is ReviewedDocumentStatus.Approved or ReviewedDocumentStatus.PendingEscalation),
            0,
            setByName,
            budget.CreatedAt,
            budget.BaseCurrencyCode,
            documents
                .Take(ActivityLimit)
                .Select(document => new BudgetWorkspaceActivityReadModel(
                    document.Id,
                    document.Reference,
                    DisplayName(document.EmployeeName),
                    document.Amount,
                    ActivityState(document.Status),
                    document.Date))
                .ToList(),
            BuildTrend(trendRows),
            BuildAudit(budget, status, setByName, utilization, available));
    }

    private static IReadOnlyList<BudgetWorkspaceTrendReadModel> BuildTrend(IReadOnlyList<BudgetRow> budgets)
    {
        return budgets
            .OrderBy(budget => budget.Year)
            .ThenBy(budget => budget.Month)
            .Select(budget => new BudgetWorkspaceTrendReadModel(
                $"Thg {budget.Month}",
                budget.AllocatedAmount + budget.CarryOverAmount,
                budget.SpentAmount,
                budget.CommittedAmount))
            .ToList();
    }

    private static IReadOnlyList<BudgetWorkspaceAuditReadModel> BuildAudit(
        BudgetRow budget,
        string status,
        string setByName,
        decimal utilization,
        decimal available)
    {
        var events = new List<BudgetWorkspaceAuditReadModel>
        {
            new(
                budget.Id,
                "created",
                "Ngân sách đã được tạo",
                setByName,
                budget.CreatedAt,
                $"Cấp phát {budget.AllocatedAmount:N0} {budget.BaseCurrencyCode} cho {PeriodLabel(budget.Month, budget.Year)}.")
        };

        if (status is "Approaching" or "Critical" or "Over")
        {
            events.Add(new BudgetWorkspaceAuditReadModel(
                Guid.NewGuid(),
                "threshold",
                status == "Over" ? "Ngân sách đã vượt giới hạn" : "Ngưỡng cảnh báo đã được kích hoạt",
                "Hệ thống",
                budget.UpdatedAt,
                status == "Over"
                    ? $"Vượt ngân sách {Math.Abs(available):N0} {budget.BaseCurrencyCode}."
                    : $"Mức sử dụng đạt {utilization:N0}%."));
        }

        return events;
    }

    private static string ResolveStatus(bool isActive, decimal available, decimal utilization)
    {
        if (!isActive) return "Archived";
        if (available < 0m) return "Over";
        if (utilization >= 95m) return "Critical";
        if (utilization >= 85m) return "Approaching";
        return "Healthy";
    }

    private static string ActivityState(ReviewedDocumentStatus status) =>
        status switch
        {
            ReviewedDocumentStatus.Approved => "Approved",
            ReviewedDocumentStatus.Rejected => "Rejected",
            ReviewedDocumentStatus.PendingEscalation => "Approved",
            ReviewedDocumentStatus.Draft => "Draft",
            _ => "Submitted"
        };

    private static string DepartmentPath(Guid departmentId, IReadOnlyList<DepartmentRow> departments)
    {
        var lookup = departments.ToDictionary(department => department.Id);
        var names = new List<string>();
        var currentId = departmentId;

        while (lookup.TryGetValue(currentId, out var current))
        {
            names.Add(current.Name);
            if (!current.ParentId.HasValue)
                break;
            currentId = current.ParentId.Value;
        }

        names.Reverse();
        return names.Count == 0 ? "Meridian Corp" : string.Join(" › ", names);
    }

    private static string ResolveSetByName(IReadOnlyList<MembershipRow> memberships)
    {
        var owner = memberships
            .Where(membership => membership.IsActive)
            .OrderByDescending(membership => membership.Role is RoleType.TenantAdmin or RoleType.SuperAdmin)
            .ThenBy(membership => membership.Email)
            .FirstOrDefault();

        return owner is null ? "Finance Manager" : DisplayName(owner.FullName ?? owner.Email);
    }

    private static string DisplayName(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !value.Contains('@'))
            return value.Trim();

        var localPart = value.Split('@')[0];
        return string.Join(' ', localPart
            .Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string PeriodLabel(int month, int year) => $"Tháng {month}/{year}";

    private sealed record DepartmentRow(
        Guid Id,
        string Name,
        Guid? ParentId,
        DateTime CreatedAt);

    private sealed record MembershipRow(
        Guid Id,
        Guid? DepartmentId,
        RoleType Role,
        bool IsActive,
        string Email,
        string? FullName);

    private sealed record BudgetRow(
        Guid Id,
        Guid DepartmentId,
        int Month,
        int Year,
        decimal AllocatedAmount,
        decimal CarryOverAmount,
        decimal CommittedAmount,
        decimal SpentAmount,
        string BaseCurrencyCode,
        BudgetEnforcementMode EnforcementMode,
        bool IsActive,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private sealed record DocumentRow(
        Guid Id,
        Guid DepartmentId,
        string Reference,
        string EmployeeName,
        decimal Amount,
        ReviewedDocumentStatus Status,
        DateTime Date);
}
