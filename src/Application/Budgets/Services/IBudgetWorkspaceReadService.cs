using FinFlow.Domain.Enums;

namespace FinFlow.Application.Budgets.Services;

public sealed record BudgetWorkspaceReadModel(
    BudgetWorkspaceSummaryReadModel Summary,
    IReadOnlyList<BudgetWorkspaceBudgetReadModel> Budgets,
    BudgetWorkspaceBudgetReadModel? SelectedBudget);

public sealed record BudgetWorkspaceSummaryReadModel(
    string PeriodLabel,
    decimal TotalAllocated,
    decimal TotalCommitted,
    decimal TotalSpent,
    decimal AvailablePool,
    int ActiveBudgetCount,
    int OverBudgetCount,
    int CommittedDocumentCount,
    int PaidDocumentCount,
    bool AllWithinBudget,
    string CurrencyCode,
    string? ManagerScopeDepartmentName);

public sealed record BudgetWorkspaceBudgetReadModel(
    Guid Id,
    Guid DepartmentId,
    string DepartmentName,
    string DepartmentPath,
    string PeriodLabel,
    decimal AllocatedAmount,
    decimal CarryOverAmount,
    decimal CommittedAmount,
    decimal SpentAmount,
    decimal AvailableAmount,
    decimal UtilizationPct,
    string EnforcementMode,
    string Status,
    bool IsActive,
    DateTime UpdatedAt,
    int ActiveExpenseCount,
    int CommittedDocumentCount,
    int PaidDocumentCount,
    string SetByName,
    DateTime SetOn,
    string CurrencyCode,
    IReadOnlyList<BudgetWorkspaceActivityReadModel> Activity,
    IReadOnlyList<BudgetWorkspaceTrendReadModel> Trend,
    IReadOnlyList<BudgetWorkspaceAuditReadModel> Audit);

public sealed record BudgetWorkspaceActivityReadModel(
    Guid Id,
    string Reference,
    string EmployeeName,
    decimal Amount,
    string State,
    DateTime Date);

public sealed record BudgetWorkspaceTrendReadModel(
    string MonthLabel,
    decimal AllocatedAmount,
    decimal SpentAmount,
    decimal? CommittedAmount);

public sealed record BudgetWorkspaceAuditReadModel(
    Guid Id,
    string Type,
    string Title,
    string ActorName,
    DateTime Timestamp,
    string? Detail);

public interface IBudgetWorkspaceReadService
{
    Task<BudgetWorkspaceReadModel> GetWorkspaceAsync(
        Guid tenantId,
        Guid requesterMembershipId,
        RoleType requesterRole,
        int month,
        int year,
        Guid? selectedBudgetId,
        CancellationToken cancellationToken = default);
}
