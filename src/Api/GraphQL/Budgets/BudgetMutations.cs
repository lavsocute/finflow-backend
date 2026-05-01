using FinFlow.Api.GraphQL.Auth;
using FinFlow.Application.Budgets.Commands.CreateBudget;
using FinFlow.Application.Budgets.Commands.UpdateBudget;
using FinFlow.Application.Budgets.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using MediatR;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using DomainError = FinFlow.Domain.Abstractions.Error;

namespace FinFlow.Api.GraphQL.Budgets;

public sealed record CreateBudgetInput(Guid DepartmentId, int Month, int Year, decimal Amount);

public sealed record UpdateBudgetInput(Guid BudgetId, decimal Amount);

public sealed record BudgetPayload(
    Guid Id,
    Guid DepartmentId,
    string DepartmentName,
    int Month,
    int Year,
    decimal AllocatedAmount,
    decimal SpentAmount,
    decimal AvailableAmount,
    bool IsOverBudget,
    bool IsNearLimit);

[ExtendObjectType(typeof(AuthMutations))]
public sealed class BudgetMutations
{
    [Authorize]
    public async Task<BudgetPayload> CreateBudgetAsync(
        CreateBudgetInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);
        var role = EnsureRole(context);
        if (role != RoleType.TenantAdmin)
            throw ToGraphQlException(new DomainError("Budget.Forbidden", "Only TenantAdmin can create budgets."));

        var result = await mediator.Send(
            new CreateBudgetCommand(scope.TenantId, input.DepartmentId, input.Month, input.Year, input.Amount),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ToPayload(result.Value);
    }

    [Authorize]
    public async Task<BudgetPayload> UpdateBudgetAsync(
        UpdateBudgetInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);
        var role = EnsureRole(context);
        if (role != RoleType.TenantAdmin)
            throw ToGraphQlException(new DomainError("Budget.Forbidden", "Only TenantAdmin can update budgets."));

        var result = await mediator.Send(
            new UpdateBudgetCommand(input.BudgetId, scope.TenantId, input.Amount),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ToPayload(result.Value);
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

    private static RoleType EnsureRole(IResolverContext context)
    {
        var user = context.Service<IHttpContextAccessor>().HttpContext?.User;
        var rawRole = user?.FindFirst(ClaimTypes.Role)?.Value
            ?? user?.FindFirst("role")?.Value;

        if (Enum.TryParse<RoleType>(rawRole, out var role))
            return role;

        throw new GraphQLException(new HotChocolate.Error("The current user is not authorized to access this resource.", "Account.Unauthorized"));
    }

    private static BudgetPayload ToPayload(BudgetDetailDto dto) =>
        new(
            dto.Id,
            dto.DepartmentId,
            dto.DepartmentName,
            dto.Month,
            dto.Year,
            dto.AllocatedAmount,
            dto.SpentAmount,
            dto.AvailableAmount,
            dto.IsOverBudget,
            dto.IsNearLimit);

    private static GraphQLException ToGraphQlException(DomainError error) =>
        new(new HotChocolate.Error(error.Description, error.Code));
}