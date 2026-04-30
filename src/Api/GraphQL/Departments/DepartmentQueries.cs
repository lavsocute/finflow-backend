using FinFlow.Application.Departments.DTOs;
using FinFlow.Application.Departments.Queries.GetDepartments;
using FinFlow.Application.Departments.Queries.GetDepartmentTree;
using FinFlow.Application.Departments.Queries.GetDepartmentMembers;
using FinFlow.Domain.Abstractions;
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
    public async Task<IReadOnlyList<DepartmentSummaryType>> GetDepartmentsAsync(
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);
        var result = await mediator.Send(new GetDepartmentsQuery(scope.TenantId), cancellationToken);
        if (result.IsFailure)
            throw ToGraphQlException(result.Error);
        return result.Value.Select(DepartmentSummaryType.FromDto).ToList();
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
}
