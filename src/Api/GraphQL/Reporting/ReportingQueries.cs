using FinFlow.Application.Reporting;
using FinFlow.Application.Reporting.DTOs;
using FinFlow.Domain.Enums;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;

namespace FinFlow.Api.GraphQL.Reporting;

[ExtendObjectType(typeof(global::Query))]
public sealed class ReportingQueries
{
    /// <summary>
    /// High-level summary for a period: total spend, breakdown by category /
    /// department / currency. Manager and above. Manager-level scoping
    /// (only own department) is enforced inside the query via their
    /// membership's department id.
    /// </summary>
    [Authorize]
    public async Task<ExpenseSummaryPayload> ExpenseSummaryAsync(
        DateOnly from,
        DateOnly to,
        Guid? departmentId,
        [Service] IReportingService reporting,
        [Service] IHttpContextAccessor http,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var (tenantId, role) = ResolveRequester(context, http);
        EnsureManagerOrAbove(role);

        var periodResult = ReportingPeriod.Create(from, to);
        if (periodResult.IsFailure)
            throw ToGraphQlException(periodResult.Error);

        var scope = ResolveDepartmentScope(role, departmentId, http);
        var dto = await reporting.GetExpenseSummaryAsync(tenantId, periodResult.Value, scope, cancellationToken);
        return ExpenseSummaryPayload.From(dto);
    }

    [Authorize]
    public async Task<IReadOnlyList<BudgetUtilizationPayload>> BudgetUtilizationAsync(
        int month,
        int year,
        Guid? departmentId,
        [Service] IReportingService reporting,
        [Service] IHttpContextAccessor http,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var (tenantId, role) = ResolveRequester(context, http);
        EnsureManagerOrAbove(role);

        if (month is < 1 or > 12)
            throw new GraphQLException(new HotChocolate.Error("Month must be 1..12.", "Reporting.InvalidMonth"));
        if (year is < 2000 or > 2100)
            throw new GraphQLException(new HotChocolate.Error("Year is out of range.", "Reporting.InvalidYear"));

        var scope = ResolveDepartmentScope(role, departmentId, http);
        var dtos = await reporting.GetBudgetUtilizationAsync(tenantId, month, year, scope, cancellationToken);
        return dtos.Select(BudgetUtilizationPayload.From).ToList();
    }

    [Authorize]
    public async Task<IReadOnlyList<TopVendorPayload>> TopVendorsAsync(
        DateOnly from,
        DateOnly to,
        int? limit,
        [Service] IReportingService reporting,
        [Service] IHttpContextAccessor http,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var (tenantId, role) = ResolveRequester(context, http);
        EnsureManagerOrAbove(role);

        var resolvedLimit = NormalizeLimit(limit);

        var periodResult = ReportingPeriod.Create(from, to);
        if (periodResult.IsFailure)
            throw ToGraphQlException(periodResult.Error);

        var dtos = await reporting.GetTopVendorsAsync(tenantId, periodResult.Value, resolvedLimit, cancellationToken);
        return dtos.Select(TopVendorPayload.From).ToList();
    }

    [Authorize]
    public async Task<IReadOnlyList<TopEmployeePayload>> TopEmployeesAsync(
        DateOnly from,
        DateOnly to,
        Guid? departmentId,
        int? limit,
        [Service] IReportingService reporting,
        [Service] IHttpContextAccessor http,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var (tenantId, role) = ResolveRequester(context, http);
        EnsureManagerOrAbove(role);

        var resolvedLimit = NormalizeLimit(limit);
        var periodResult = ReportingPeriod.Create(from, to);
        if (periodResult.IsFailure)
            throw ToGraphQlException(periodResult.Error);

        var scope = ResolveDepartmentScope(role, departmentId, http);
        var dtos = await reporting.GetTopEmployeesAsync(tenantId, periodResult.Value, scope, resolvedLimit, cancellationToken);
        return dtos.Select(TopEmployeePayload.From).ToList();
    }

    /// <summary>
    /// Pending payment queue — Accountant + TenantAdmin only. Sorted oldest
    /// first so frontend can highlight overdue items.
    /// </summary>
    [Authorize]
    public async Task<IReadOnlyList<PendingPaymentItemPayload>> PendingPaymentQueueAsync(
        [Service] IReportingService reporting,
        [Service] IHttpContextAccessor http,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var (tenantId, role) = ResolveRequester(context, http);
        EnsureAccountantOrAdmin(role);

        var dtos = await reporting.GetPendingPaymentQueueAsync(tenantId, cancellationToken);
        return dtos.Select(PendingPaymentItemPayload.From).ToList();
    }

    [Authorize]
    public async Task<IReadOnlyList<MonthlyTrendPointPayload>> MonthlyTrendAsync(
        int? months,
        Guid? departmentId,
        [Service] IReportingService reporting,
        [Service] IHttpContextAccessor http,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var (tenantId, role) = ResolveRequester(context, http);
        EnsureManagerOrAbove(role);

        var resolvedMonths = months ?? 6;
        if (resolvedMonths is < 1 or > ReportingPeriod.MaxMonths)
            throw ToGraphQlException(ReportingErrors.InvalidMonthRange);

        var scope = ResolveDepartmentScope(role, departmentId, http);
        var dtos = await reporting.GetMonthlyTrendAsync(tenantId, resolvedMonths, scope, cancellationToken);
        return dtos.Select(MonthlyTrendPointPayload.From).ToList();
    }

    // ─────────────────────────────────────────── helpers
    private static (Guid TenantId, RoleType Role) ResolveRequester(IResolverContext context, IHttpContextAccessor http)
    {
        var user = http.HttpContext?.User;
        var tenantRaw = user?.FindFirst("IdTenant")?.Value;
        if (!Guid.TryParse(tenantRaw, out var tenantId))
            throw new GraphQLException(new HotChocolate.Error("Tenant context missing.", "Account.Unauthorized"));

        var roleRaw = user?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
            ?? user?.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value;
        if (!Enum.TryParse<RoleType>(roleRaw, out var role))
            throw new GraphQLException(new HotChocolate.Error("Role claim missing or invalid.", "Account.Unauthorized"));

        return (tenantId, role);
    }

    private static void EnsureManagerOrAbove(RoleType role)
    {
        if (role is RoleType.Manager or RoleType.Accountant or RoleType.TenantAdmin or RoleType.SuperAdmin)
            return;
        throw new GraphQLException(new HotChocolate.Error(
            "Only Manager, Accountant, or Admin can access reporting.", "Reporting.Forbidden"));
    }

    private static void EnsureAccountantOrAdmin(RoleType role)
    {
        if (role is RoleType.Accountant or RoleType.TenantAdmin or RoleType.SuperAdmin)
            return;
        throw new GraphQLException(new HotChocolate.Error(
            "Only Accountant or Admin can view the pending payment queue.", "Reporting.Forbidden"));
    }

    /// <summary>
    /// Manager sees only their own department. Accountant/TenantAdmin can
    /// optionally pass a departmentId filter, otherwise see whole tenant.
    /// </summary>
    private static Guid? ResolveDepartmentScope(RoleType role, Guid? requestedDepartmentId, IHttpContextAccessor http)
    {
        if (role == RoleType.Manager)
        {
            var deptRaw = http.HttpContext?.User?.FindFirst("IdDepartment")?.Value;
            if (Guid.TryParse(deptRaw, out var deptId))
                return deptId;
            // Manager without a department claim cannot see any data.
            throw new GraphQLException(new HotChocolate.Error(
                "Manager must be assigned to a department to access reporting.", "Reporting.Forbidden"));
        }
        return requestedDepartmentId;
    }

    private static int NormalizeLimit(int? limit)
    {
        var resolved = limit ?? 10;
        if (resolved is < 1 or > 100)
            throw ToGraphQlException(ReportingErrors.LimitOutOfRange);
        return resolved;
    }

    private static GraphQLException ToGraphQlException(FinFlow.Domain.Abstractions.Error error) =>
        new(new HotChocolate.Error(error.Description, error.Code));
}
