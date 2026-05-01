using FinFlow.Application.Expenses.Queries.GetExpenses;
using FinFlow.Application.Expenses.Queries.GetExpenseSummary;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Expenses;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using MediatR;
using Microsoft.AspNetCore.Http;
using DomainError = FinFlow.Domain.Abstractions.Error;

namespace FinFlow.Api.GraphQL.Expenses;

[ExtendObjectType(typeof(global::Query))]
public sealed class ExpenseQueries
{
    [Authorize]
    public async Task<IReadOnlyList<ExpensePayload>> ExpensesAsync(
        Guid? departmentId,
        int? month,
        int? year,
        string? status,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);

        ExpenseStatus? expenseStatus = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ExpenseStatus>(status, ignoreCase: true, out var parsed))
        {
            expenseStatus = parsed;
        }

        var result = await mediator.Send(
            new GetExpensesQuery(scope.TenantId, departmentId, month, year, null, expenseStatus),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return result.Value
            .Select(ExpensePayload.FromSummary)
            .ToList();
    }

    [Authorize]
    public async Task<ExpenseSummaryPayload?> ExpenseSummaryAsync(
        Guid departmentId,
        int month,
        int year,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);

        var result = await mediator.Send(
            new GetExpenseSummaryQuery(scope.TenantId, departmentId, month, year),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return result.Value is null ? null : ExpenseSummaryPayload.FromDto(result.Value);
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