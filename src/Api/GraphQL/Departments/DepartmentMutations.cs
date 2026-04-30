using FinFlow.Api.GraphQL.Auth;
using FinFlow.Application.Departments.Commands.ChangeParentDepartment;
using FinFlow.Application.Departments.Commands.CreateDepartment;
using FinFlow.Application.Departments.Commands.DeactivateDepartment;
using FinFlow.Application.Departments.Commands.RenameDepartment;
using FinFlow.Application.Departments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using MediatR;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using DomainError = FinFlow.Domain.Abstractions.Error;

namespace FinFlow.Api.GraphQL.Departments;

public sealed record CreateDepartmentInput(string Name, Guid? ParentId);

public sealed record DepartmentPayload(
    Guid Id,
    string Name,
    Guid? ParentId,
    bool IsActive);

[ExtendObjectType(typeof(AuthMutations))]
public sealed class DepartmentMutations
{
    [Authorize]
    public async Task<DepartmentPayload> CreateDepartmentAsync(
        CreateDepartmentInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);
        var role = EnsureRole(context);
        if (role != RoleType.TenantAdmin)
            throw ToGraphQlException(new DomainError("Department.Forbidden", "Only TenantAdmin can create departments."));

        var result = await mediator.Send(
            new CreateDepartmentCommand(scope.TenantId, input.Name, input.ParentId),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ToPayload(result.Value);
    }

    [Authorize]
    public async Task<DepartmentPayload> RenameDepartmentAsync(
        RenameDepartmentInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);
        var role = EnsureRole(context);
        if (role != RoleType.TenantAdmin)
            throw ToGraphQlException(new DomainError("Department.Forbidden", "Only TenantAdmin can rename departments."));

        var result = await mediator.Send(
            new RenameDepartmentCommand(input.Id, scope.TenantId, input.Name),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ToPayload(result.Value);
    }

    [Authorize]
    public async Task<DepartmentPayload> ChangeParentDepartmentAsync(
        ChangeParentDepartmentInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);
        var role = EnsureRole(context);
        if (role != RoleType.TenantAdmin)
            throw ToGraphQlException(new DomainError("Department.Forbidden", "Only TenantAdmin can change parent department."));

        var result = await mediator.Send(
            new ChangeParentDepartmentCommand(input.Id, scope.TenantId, input.NewParentId),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ToPayload(result.Value);
    }

    [Authorize]
    public async Task<bool> DeactivateDepartmentAsync(
        DeactivateDepartmentInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);
        var role = EnsureRole(context);
        if (role != RoleType.TenantAdmin)
            throw ToGraphQlException(new DomainError("Department.Forbidden", "Only TenantAdmin can deactivate departments."));

        var result = await mediator.Send(
            new DeactivateDepartmentCommand(input.Id, scope.TenantId),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return true;
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

    private static DepartmentPayload ToPayload(DepartmentSummaryDto dto) =>
        new(dto.Id, dto.Name, dto.ParentId, dto.IsActive);

    private static GraphQLException ToGraphQlException(DomainError error) =>
        new(new HotChocolate.Error(error.Description, error.Code));
}

public sealed record RenameDepartmentInput(Guid Id, string Name);

public sealed record ChangeParentDepartmentInput(Guid Id, Guid? NewParentId);

public sealed record DeactivateDepartmentInput(Guid Id);