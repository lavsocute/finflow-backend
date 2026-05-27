using FinFlow.Application.Departments.Services;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Departments;

internal sealed class DepartmentWorkspaceReadService : IDepartmentWorkspaceReadService
{
    private const int MemberPreviewLimit = 4;
    private const int ActivityLimit = 5;
    private readonly ApplicationDbContext _dbContext;

    public DepartmentWorkspaceReadService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DepartmentWorkspaceReadModel> GetWorkspaceAsync(
        Guid tenantId,
        Guid requesterMembershipId,
        Guid? selectedDepartmentId,
        CancellationToken cancellationToken = default)
    {
        var departments = await _dbContext.Set<Department>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(department => department.IdTenant == tenantId)
            .OrderBy(department => department.CreatedAt)
            .Select(department => new DepartmentRow(
                department.Id,
                department.Name,
                department.ParentId,
                department.IsActive,
                department.CreatedAt))
            .ToListAsync(cancellationToken);

        var memberships = await (
            from membership in _dbContext.Set<TenantMembership>().AsNoTracking().IgnoreQueryFilters()
            join account in _dbContext.Set<Account>().AsNoTracking().IgnoreQueryFilters()
                on membership.AccountId equals account.Id
            where membership.IdTenant == tenantId
            select new MembershipRow(
                membership.Id,
                membership.AccountId,
                membership.DepartmentId,
                membership.Role.ToString(),
                membership.Role,
                membership.IsActive,
                account.Email,
                account.FullName))
            .ToListAsync(cancellationToken);

        var budgets = await _dbContext.Budgets
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(budget => budget.IdTenant == tenantId)
            .Select(budget => new BudgetRow(
                budget.IdDepartment,
                budget.Month,
                budget.Year,
                budget.AllocatedAmount,
                budget.SpentAmount,
                budget.IsActive))
            .ToListAsync(cancellationToken);

        var documents = await _dbContext.ReviewedDocuments
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(document => document.IdTenant == tenantId)
            .OrderByDescending(document => document.SubmittedAt)
            .Select(document => new DocumentRow(
                document.Id,
                document.IdDepartment,
                document.Reference,
                document.VendorName,
                document.TotalAmount,
                document.ReviewedByStaff))
            .ToListAsync(cancellationToken);

        var requesterDepartmentId = memberships
            .FirstOrDefault(membership => membership.MembershipId == requesterMembershipId)
            ?.DepartmentId;
        var resolvedSelectedId = ResolveSelectedDepartmentId(
            departments,
            selectedDepartmentId,
            requesterDepartmentId);

        var memberCounts = memberships
            .Where(membership => membership.DepartmentId.HasValue && membership.IsActive)
            .GroupBy(membership => membership.DepartmentId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var childrenLookup = departments
            .GroupBy(department => department.ParentId)
            .ToLookup(group => group.Key, group => group.ToList());
        var budgetLookup = budgets
            .Where(budget => budget.IsActive)
            .GroupBy(budget => budget.DepartmentId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(budget => budget.Year)
                    .ThenByDescending(budget => budget.Month)
                    .First());
        var documentLookup = documents
            .GroupBy(document => document.DepartmentId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var tree = departments
            .Where(department => department.ParentId is null)
            .Select(department => BuildTreeNode(
                department,
                childrenLookup,
                memberCounts,
                budgetLookup))
            .ToList();

        var selectedDepartment = resolvedSelectedId.HasValue
            ? BuildSelectedDepartment(
                departments,
                memberships,
                childrenLookup,
                memberCounts,
                budgetLookup,
                documentLookup,
                resolvedSelectedId.Value)
            : null;

        return new DepartmentWorkspaceReadModel(
            new DepartmentWorkspaceSummaryReadModel(
                TotalDepartments: departments.Count,
                TotalMembers: memberships.Count(membership => membership.IsActive),
                ActiveDepartments: departments.Count(department => department.IsActive),
                SelectedDepartmentId: resolvedSelectedId),
            tree,
            selectedDepartment);
    }

    private static Guid? ResolveSelectedDepartmentId(
        IReadOnlyList<DepartmentRow> departments,
        Guid? requestedDepartmentId,
        Guid? requesterDepartmentId)
    {
        if (requestedDepartmentId.HasValue &&
            departments.Any(department => department.Id == requestedDepartmentId.Value))
            return requestedDepartmentId;

        if (requesterDepartmentId.HasValue &&
            departments.Any(department => department.Id == requesterDepartmentId.Value))
            return requesterDepartmentId;

        return departments.FirstOrDefault(department => department.ParentId is null)?.Id
            ?? departments.FirstOrDefault()?.Id;
    }

    private static DepartmentWorkspaceTreeNodeReadModel BuildTreeNode(
        DepartmentRow department,
        ILookup<Guid?, List<DepartmentRow>> childrenLookup,
        IReadOnlyDictionary<Guid, int> memberCounts,
        IReadOnlyDictionary<Guid, BudgetRow> budgetLookup)
    {
        var directChildren = childrenLookup[department.Id].SelectMany(children => children).ToList();
        var children = directChildren
            .Select(child => BuildTreeNode(child, childrenLookup, memberCounts, budgetLookup))
            .ToList();

        return new DepartmentWorkspaceTreeNodeReadModel(
            department.Id,
            department.Name,
            department.ParentId,
            department.IsActive,
            memberCounts.GetValueOrDefault(department.Id),
            children.Count,
            BudgetUtilization(budgetLookup.GetValueOrDefault(department.Id)),
            children);
    }

    private static DepartmentWorkspaceSelectedDepartmentReadModel? BuildSelectedDepartment(
        IReadOnlyList<DepartmentRow> departments,
        IReadOnlyList<MembershipRow> memberships,
        ILookup<Guid?, List<DepartmentRow>> childrenLookup,
        IReadOnlyDictionary<Guid, int> memberCounts,
        IReadOnlyDictionary<Guid, BudgetRow> budgetLookup,
        IReadOnlyDictionary<Guid, List<DocumentRow>> documentLookup,
        Guid selectedDepartmentId)
    {
        var department = departments.FirstOrDefault(item => item.Id == selectedDepartmentId);
        if (department is null)
            return null;

        var directChildren = childrenLookup[department.Id].SelectMany(children => children).ToList();
        var departmentMembers = memberships
            .Where(membership => membership.DepartmentId == department.Id)
            .OrderByDescending(membership => IsManagerRole(membership.RoleKind))
            .ThenBy(membership => membership.Email)
            .ToList();
        var manager = departmentMembers.FirstOrDefault(membership => IsManagerRole(membership.RoleKind));
        var documents = documentLookup.GetValueOrDefault(department.Id) ?? [];
        var budget = budgetLookup.GetValueOrDefault(department.Id);

        return new DepartmentWorkspaceSelectedDepartmentReadModel(
            department.Id,
            department.Name,
            departments.FirstOrDefault(item => item.Id == department.ParentId)?.Name,
            $"DEPT-{department.Id.ToString("N")[..6].ToUpperInvariant()}",
            department.IsActive ? "Active" : "Inactive",
            department.CreatedAt,
            memberCounts.GetValueOrDefault(department.Id),
            directChildren.Count,
            documents.Count == 0 ? null : documents.Sum(document => document.Amount),
            documents.Count == 0 ? null : documents.Count,
            manager is null ? null : ToManager(manager),
            budget is null ? null : ToBudgetSnapshot(budget),
            directChildren
                .Select(child => new DepartmentWorkspaceSubDepartmentReadModel(
                    child.Id,
                    child.Name,
                    memberCounts.GetValueOrDefault(child.Id),
                    BudgetUtilization(budgetLookup.GetValueOrDefault(child.Id))))
                .ToList(),
            departmentMembers
                .Take(MemberPreviewLimit)
                .Select(ToMemberPreview)
                .ToList(),
            documents
                .Take(ActivityLimit)
                .Select(document => new DepartmentWorkspaceActivityReadModel(
                    document.Id,
                    $"{document.Reference} đã gửi",
                    document.VendorName,
                    document.ActorName,
                    "info",
                    document.Amount))
                .ToList());
    }

    private static bool IsManagerRole(RoleType role) =>
        role is RoleType.Manager or RoleType.TenantAdmin;

    private static DepartmentWorkspaceManagerReadModel ToManager(MembershipRow membership) =>
        new(
            membership.MembershipId,
            DisplayName(membership),
            membership.Email,
            membership.Role,
            Initials(DisplayName(membership)));

    private static DepartmentWorkspaceMemberPreviewReadModel ToMemberPreview(MembershipRow membership) =>
        new(
            membership.MembershipId,
            DisplayName(membership),
            membership.Email,
            membership.Role,
            Initials(DisplayName(membership)),
            membership.IsActive);

    private static DepartmentWorkspaceBudgetSnapshotReadModel ToBudgetSnapshot(BudgetRow budget)
    {
        var utilization = BudgetUtilization(budget) ?? 0m;
        return new DepartmentWorkspaceBudgetSnapshotReadModel(
            $"Tháng {budget.Month}/{budget.Year}",
            budget.AllocatedAmount,
            budget.SpentAmount,
            budget.AllocatedAmount - budget.SpentAmount,
            utilization);
    }

    private static decimal? BudgetUtilization(BudgetRow? budget)
    {
        if (budget is null || budget.AllocatedAmount <= 0)
            return null;

        return Math.Round(budget.SpentAmount / budget.AllocatedAmount * 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static string DisplayName(MembershipRow membership)
    {
        if (!string.IsNullOrWhiteSpace(membership.FullName))
            return membership.FullName.Trim();

        var localPart = membership.Email.Split('@')[0];
        return string.Join(' ', localPart
            .Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string Initials(string name)
    {
        var parts = name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .Select(part => char.ToUpperInvariant(part[0]))
            .ToArray();

        return parts.Length == 0 ? "FF" : new string(parts);
    }

    private sealed record DepartmentRow(
        Guid Id,
        string Name,
        Guid? ParentId,
        bool IsActive,
        DateTime CreatedAt);

    private sealed record MembershipRow(
        Guid MembershipId,
        Guid AccountId,
        Guid? DepartmentId,
        string Role,
        RoleType RoleKind,
        bool IsActive,
        string Email,
        string? FullName);

    private sealed record BudgetRow(
        Guid DepartmentId,
        int Month,
        int Year,
        decimal AllocatedAmount,
        decimal SpentAmount,
        bool IsActive);

    private sealed record DocumentRow(
        Guid Id,
        Guid DepartmentId,
        string Reference,
        string VendorName,
        decimal Amount,
        string ActorName);
}
