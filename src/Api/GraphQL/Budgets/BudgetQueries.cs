using FinFlow.Application.Budgets.DTOs;
using FinFlow.Application.Budgets.Queries.GetBudgets;
using FinFlow.Application.Budgets.Queries.GetBudgetByDepartment;
using FinFlow.Application.Budgets.Queries.CheckBudgetAvailable;
using FinFlow.Domain.Abstractions;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using MediatR;
using Microsoft.AspNetCore.Http;
using DomainError = FinFlow.Domain.Abstractions.Error;

namespace FinFlow.Api.GraphQL.Budgets;

[ExtendObjectType(typeof(global::Query))]
public sealed class BudgetQueries
{
    [Authorize]
    public async Task<IReadOnlyList<BudgetSummaryType>> GetBudgetsAsync(
        int? month,
        int? year,
        Guid? departmentId,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);
        var result = await mediator.Send(
            new GetBudgetsQuery(scope.TenantId, month, year, departmentId),
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
        var scope = EnsureAuthorizedWorkspace(context);
        var result = await mediator.Send(
            new GetBudgetByDepartmentQuery(scope.TenantId, departmentId, month, year),
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
        var scope = EnsureAuthorizedWorkspace(context);
        var result = await mediator.Send(
            new CheckBudgetAvailableQuery(scope.TenantId, departmentId, month, year),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return BudgetCheckType.FromDto(result.Value);
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
}