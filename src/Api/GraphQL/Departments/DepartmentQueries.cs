using FinFlow.Application.Departments.DTOs;
using FinFlow.Application.Departments.Queries.GetDepartments;
using FinFlow.Application.Departments.Queries.GetDepartmentTree;
using FinFlow.Application.Departments.Queries.GetDepartmentMembers;
using FinFlow.Application.Departments.Services;
using FinFlow.Domain.Abstractions;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using MediatR;
using Microsoft.AspNetCore.Http;
using DomainError = FinFlow.Domain.Abstractions.Error;

namespace FinFlow.Api.GraphQL.Departments;

[ExtendObjectType(typeof(global::Query))]
public sealed class DepartmentQueries
{
    [Authorize]
    public async Task<DepartmentWorkspacePayload> DepartmentWorkspaceAsync(
        Guid? selectedDepartmentId,
        [Service] IDepartmentWorkspaceReadService departmentWorkspaceReadService,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);
        var workspace = await departmentWorkspaceReadService.GetWorkspaceAsync(
            scope.TenantId,
            scope.MembershipId,
            selectedDepartmentId,
            cancellationToken);

        return DepartmentWorkspacePayload.FromReadModel(workspace);
    }

    [Authorize]
    public async Task<IReadOnlyList<DepartmentSummaryType>> GetDepartmentsAsync(
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        return await LoadDepartmentsAsync(mediator, context, cancellationToken);
    }

    [Authorize]
    [GraphQLName("getDepartments")]
    public Task<IReadOnlyList<DepartmentSummaryType>> GetDepartmentsLegacyAsync(
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        return LoadDepartmentsAsync(mediator, context, cancellationToken);
    }

    [Authorize]
    public async Task<IReadOnlyList<DepartmentTreeNodeType>> GetDepartmentTreeAsync(
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);
        var result = await mediator.Send(new GetDepartmentTreeQuery(scope.TenantId), cancellationToken);
        if (result.IsFailure)
            throw ToGraphQlException(result.Error);
        return result.Value.Select(DepartmentTreeNodeType.FromDto).ToList();
    }

    [Authorize]
    public async Task<IReadOnlyList<DepartmentMemberType>> GetDepartmentMembersAsync(
        Guid departmentId,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);
        var result = await mediator.Send(new GetDepartmentMembersQuery(scope.TenantId, departmentId), cancellationToken);
        if (result.IsFailure)
            throw ToGraphQlException(result.Error);
        return result.Value.Select(DepartmentMemberType.FromDto).ToList();
    }

    private static (Guid TenantId, Guid MembershipId) EnsureAuthorizedWorkspace(IResolverContext context)
    {
        var tenantId = GetRequiredGuidClaim(context, "IdTenant");
        var membershipId = GetRequiredGuidClaim(context, "MembershipId");
        return (tenantId, membershipId);
    }

    private static Guid GetRequiredGuidClaim(IResolverContext context, string claimType)
    {
        var user = context.Service<IHttpContextAccessor>().HttpContext?.User;
        var rawValue = user?.FindFirst(claimType)?.Value;

        if (Guid.TryParse(rawValue, out var value))
            return value;

        throw new GraphQLException(new HotChocolate.Error("The current user is not authorized to access this resource.", "Account.Unauthorized"));
    }

    private static GraphQLException ToGraphQlException(DomainError error) =>
        new(new HotChocolate.Error(error.Description, error.Code));

    private static async Task<IReadOnlyList<DepartmentSummaryType>> LoadDepartmentsAsync(
        IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);
        var result = await mediator.Send(new GetDepartmentsQuery(scope.TenantId), cancellationToken);
        if (result.IsFailure)
            throw ToGraphQlException(result.Error);
        return result.Value.Select(DepartmentSummaryType.FromDto).ToList();
    }
}

public sealed record DepartmentWorkspacePayload(
    DepartmentWorkspaceSummaryPayload Summary,
    IReadOnlyList<DepartmentWorkspaceTreeNodePayload> Tree,
    DepartmentWorkspaceSelectedDepartmentPayload? SelectedDepartment)
{
    public static DepartmentWorkspacePayload FromReadModel(DepartmentWorkspaceReadModel model) =>
        new(
            new DepartmentWorkspaceSummaryPayload(
                model.Summary.TotalDepartments,
                model.Summary.TotalMembers,
                model.Summary.ActiveDepartments,
                model.Summary.SelectedDepartmentId),
            model.Tree.Select(DepartmentWorkspaceTreeNodePayload.FromReadModel).ToList(),
            model.SelectedDepartment is null
                ? null
                : DepartmentWorkspaceSelectedDepartmentPayload.FromReadModel(model.SelectedDepartment));
}

public sealed record DepartmentWorkspaceSummaryPayload(
    int TotalDepartments,
    int TotalMembers,
    int ActiveDepartments,
    Guid? SelectedDepartmentId);

public sealed record DepartmentWorkspaceTreeNodePayload(
    Guid Id,
    string Name,
    Guid? ParentId,
    bool IsActive,
    int MemberCount,
    int ChildCount,
    decimal? BudgetUtilizationPct,
    IReadOnlyList<DepartmentWorkspaceTreeNodePayload> Children)
{
    public static DepartmentWorkspaceTreeNodePayload FromReadModel(DepartmentWorkspaceTreeNodeReadModel model) =>
        new(
            model.Id,
            model.Name,
            model.ParentId,
            model.IsActive,
            model.MemberCount,
            model.ChildCount,
            model.BudgetUtilizationPct,
            model.Children.Select(FromReadModel).ToList());
}

public sealed record DepartmentWorkspaceSelectedDepartmentPayload(
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
    DepartmentWorkspaceManagerPayload? Manager,
    DepartmentWorkspaceBudgetSnapshotPayload? BudgetSnapshot,
    IReadOnlyList<DepartmentWorkspaceSubDepartmentPayload> SubDepartments,
    IReadOnlyList<DepartmentWorkspaceMemberPreviewPayload> MembersPreview,
    IReadOnlyList<DepartmentWorkspaceActivityPayload> RecentActivity)
{
    public static DepartmentWorkspaceSelectedDepartmentPayload FromReadModel(DepartmentWorkspaceSelectedDepartmentReadModel model) =>
        new(
            model.Id,
            model.Name,
            model.ParentName,
            model.DepartmentCode,
            model.Status,
            model.CreatedAt,
            model.MemberCount,
            model.SubDepartmentCount,
            model.ExpenseVolumeAmount,
            model.ExpenseCount,
            model.Manager is null
                ? null
                : new DepartmentWorkspaceManagerPayload(
                    model.Manager.MembershipId,
                    model.Manager.FullName,
                    model.Manager.Email,
                    model.Manager.Role,
                    model.Manager.Initials),
            model.BudgetSnapshot is null
                ? null
                : new DepartmentWorkspaceBudgetSnapshotPayload(
                    model.BudgetSnapshot.PeriodLabel,
                    model.BudgetSnapshot.AllocatedAmount,
                    model.BudgetSnapshot.SpentAmount,
                    model.BudgetSnapshot.RemainingAmount,
                    model.BudgetSnapshot.UtilizationPct),
            model.SubDepartments
                .Select(item => new DepartmentWorkspaceSubDepartmentPayload(
                    item.Id,
                    item.Name,
                    item.MemberCount,
                    item.BudgetUtilizationPct))
                .ToList(),
            model.MembersPreview
                .Select(item => new DepartmentWorkspaceMemberPreviewPayload(
                    item.MembershipId,
                    item.FullName,
                    item.Email,
                    item.Role,
                    item.Initials,
                    item.IsActive))
                .ToList(),
            model.RecentActivity
                .Select(item => new DepartmentWorkspaceActivityPayload(
                    item.Id,
                    item.Title,
                    item.Description,
                    item.ActorName,
                    item.Tone,
                    item.Amount))
                .ToList());
}

public sealed record DepartmentWorkspaceManagerPayload(
    Guid MembershipId,
    string FullName,
    string Email,
    string Role,
    string Initials);

public sealed record DepartmentWorkspaceBudgetSnapshotPayload(
    string PeriodLabel,
    decimal AllocatedAmount,
    decimal SpentAmount,
    decimal RemainingAmount,
    decimal UtilizationPct);

public sealed record DepartmentWorkspaceSubDepartmentPayload(
    Guid Id,
    string Name,
    int MemberCount,
    decimal? BudgetUtilizationPct);

public sealed record DepartmentWorkspaceMemberPreviewPayload(
    Guid MembershipId,
    string FullName,
    string Email,
    string Role,
    string Initials,
    bool IsActive);

public sealed record DepartmentWorkspaceActivityPayload(
    Guid Id,
    string Title,
    string Description,
    string ActorName,
    string Tone,
    decimal? Amount);
