using FinFlow.Application.Budgets.DTOs;
using FinFlow.Application.Budgets.Queries.GetBudgets;
using FinFlow.Application.Budgets.Queries.GetBudgetByDepartment;
using FinFlow.Application.Budgets.Queries.CheckBudgetAvailable;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using MediatR;
using Microsoft.AspNetCore.Http;
using DomainError = FinFlow.Domain.Abstractions.Error;

namespace FinFlow.Api.GraphQL.Budgets;

[ExtendObjectType(typeof(global::Query))]
public sealed class BudgetQueries
{
    /// <summary>
    /// List budgets the caller is authorized to see:
    /// - TenantAdmin / Accountant: anything in tenant.
    /// - Manager: only their own department.
    /// - Staff: 403.
    /// </summary>
    [Authorize]
    public async Task<IReadOnlyList<BudgetSummaryType>> GetBudgetsAsync(
        int? month,
        int? year,
        Guid? departmentId,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var requester = ResolveRequester(context);
        var resolvedDepartmentId = ApplyDepartmentScope(requester, departmentId);

        var result = await mediator.Send(
            new GetBudgetsQuery(requester.TenantId, month, year, resolvedDepartmentId),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return result.Value.Select(BudgetSummaryType.FromDto).ToList();
    }

    [Authorize]
    public async Task<BudgetDetailType?> GetBudgetByDepartmentAsync(
        Guid departmentId,
        int month,
        int year,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var requester = ResolveRequester(context);
        EnsureCanAccessDepartment(requester, departmentId);

        var result = await mediator.Send(
            new GetBudgetByDepartmentQuery(requester.TenantId, departmentId, month, year),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return result.Value == null ? null : BudgetDetailType.FromDto(result.Value);
    }

    [Authorize]
    public async Task<BudgetCheckType> GetCheckBudgetAvailableAsync(
        Guid departmentId,
        int month,
        int year,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var requester = ResolveRequester(context);
        EnsureCanAccessDepartment(requester, departmentId);

        var result = await mediator.Send(
            new CheckBudgetAvailableQuery(requester.TenantId, departmentId, month, year),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return BudgetCheckType.FromDto(result.Value);
    }

    // ─── auth helpers ───
    private sealed record Requester(Guid TenantId, Guid MembershipId, RoleType Role, Guid? DepartmentId);

    private static Requester ResolveRequester(IResolverContext context)
    {
        var user = context.Service<IHttpContextAccessor>().HttpContext?.User;
        var tenantRaw = user?.FindFirst("IdTenant")?.Value;
        var membershipRaw = user?.FindFirst("MembershipId")?.Value;
        var roleRaw = user?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
            ?? user?.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value;
        var deptRaw = user?.FindFirst("IdDepartment")?.Value;

        if (!Guid.TryParse(tenantRaw, out var tenantId)
            || !Guid.TryParse(membershipRaw, out var membershipId)
            || !Enum.TryParse<RoleType>(roleRaw, out var role))
            throw new GraphQLException(new HotChocolate.Error("The current user is not authorized to access this resource.", "Account.Unauthorized"));

        Guid? deptId = Guid.TryParse(deptRaw, out var d) ? d : null;
        EnsureRoleAllowed(role);
        return new Requester(tenantId, membershipId, role, deptId);
    }

    private static void EnsureRoleAllowed(RoleType role)
    {
        // Staff has no business reading budgets.
        if (role is RoleType.Manager or RoleType.Accountant or RoleType.TenantAdmin or RoleType.SuperAdmin)
            return;
        throw new GraphQLException(new HotChocolate.Error(
            "Only Manager, Accountant, or Admin can view budgets.", "Budget.Forbidden"));
    }

    /// <summary>
    /// For list queries: Manager is force-scoped to own department; admins
    /// keep the caller-supplied filter (or no filter for tenant-wide view).
    /// </summary>
    private static Guid? ApplyDepartmentScope(Requester requester, Guid? requested)
    {
        if (requester.Role == RoleType.Manager)
        {
            if (!requester.DepartmentId.HasValue)
                throw new GraphQLException(new HotChocolate.Error(
                    "Manager must be assigned to a department to view budgets.", "Budget.Forbidden"));
            // If caller asked for a different dept, deny rather than silently ignore.
            if (requested.HasValue && requested.Value != requester.DepartmentId.Value)
                throw new GraphQLException(new HotChocolate.Error(
                    "Manager can only view their own department's budgets.", "Budget.Forbidden"));
            return requester.DepartmentId.Value;
        }
        return requested;
    }

    /// <summary>
    /// For per-department queries: Manager must be querying their own dept.
    /// </summary>
    private static void EnsureCanAccessDepartment(Requester requester, Guid departmentId)
    {
        if (requester.Role == RoleType.Manager)
        {
            if (!requester.DepartmentId.HasValue || requester.DepartmentId.Value != departmentId)
                throw new GraphQLException(new HotChocolate.Error(
                    "Manager can only view their own department's budgets.", "Budget.Forbidden"));
        }
    }

    private static GraphQLException ToGraphQlException(DomainError error) =>
        new(new HotChocolate.Error(error.Description, error.Code));
}
