namespace FinFlow.Application.Departments.Services;

public sealed record DepartmentWorkspaceReadModel(
    DepartmentWorkspaceSummaryReadModel Summary,
    IReadOnlyList<DepartmentWorkspaceTreeNodeReadModel> Tree,
    DepartmentWorkspaceSelectedDepartmentReadModel? SelectedDepartment);

public sealed record DepartmentWorkspaceSummaryReadModel(
    int TotalDepartments,
    int TotalMembers,
    int ActiveDepartments,
    Guid? SelectedDepartmentId);

public sealed record DepartmentWorkspaceTreeNodeReadModel(
    Guid Id,
    string Name,
    Guid? ParentId,
    bool IsActive,
    int MemberCount,
    int ChildCount,
    decimal? BudgetUtilizationPct,
    IReadOnlyList<DepartmentWorkspaceTreeNodeReadModel> Children);

public sealed record DepartmentWorkspaceSelectedDepartmentReadModel(
    Guid Id,
    string Name,
    string? ParentName,
    string? DepartmentCode,
    string Status,
    DateTime CreatedAt,
    int MemberCount,
    int SubDepartmentCount,
    decimal? ExpenseVolumeAmount,
    int? ExpenseCount,
    DepartmentWorkspaceManagerReadModel? Manager,
    DepartmentWorkspaceBudgetSnapshotReadModel? BudgetSnapshot,
    IReadOnlyList<DepartmentWorkspaceSubDepartmentReadModel> SubDepartments,
    IReadOnlyList<DepartmentWorkspaceMemberPreviewReadModel> MembersPreview,
    IReadOnlyList<DepartmentWorkspaceActivityReadModel> RecentActivity);

public sealed record DepartmentWorkspaceManagerReadModel(
    Guid MembershipId,
    string FullName,
    string Email,
    string Role,
    string Initials);

public sealed record DepartmentWorkspaceBudgetSnapshotReadModel(
    string PeriodLabel,
    decimal AllocatedAmount,
    decimal SpentAmount,
    decimal RemainingAmount,
    decimal UtilizationPct);

public sealed record DepartmentWorkspaceSubDepartmentReadModel(
    Guid Id,
    string Name,
    int MemberCount,
    decimal? BudgetUtilizationPct);

public sealed record DepartmentWorkspaceMemberPreviewReadModel(
    Guid MembershipId,
    string FullName,
    string Email,
    string Role,
    string Initials,
    bool IsActive);

public sealed record DepartmentWorkspaceActivityReadModel(
    Guid Id,
    string Title,
    string Description,
    string ActorName,
    string Tone,
    decimal? Amount);

public interface IDepartmentWorkspaceReadService
{
    Task<DepartmentWorkspaceReadModel> GetWorkspaceAsync(
        Guid tenantId,
        Guid requesterMembershipId,
        Guid? selectedDepartmentId,
        CancellationToken cancellationToken = default);
}
