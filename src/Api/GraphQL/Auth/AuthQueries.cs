using System.Security.Claims;
using FinFlow.Application.Auth.Queries.GetMyWorkspaces;
using FinFlow.Application.Auth.Queries.GetCurrentWorkspace;
using FinFlow.Application.Tenant.DTOs.Responses;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using MediatR;
using FinFlow.Application.Tenant.Queries.GetPendingTenantRequests;
using FinFlow.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace FinFlow.Api.GraphQL.Auth;

public record CurrentWorkspacePayload(
    Guid AccountId,
    string Email,
    Guid MembershipId,
    RoleType Role,
    Guid TenantId,
    string TenantCode,
    string TenantName
);

public record PendingTenantApprovalPayload(
    Guid RequestId,
    string TenantCode,
    string Name,
    string CompanyName,
    string TaxCode,
    string? RequestedByEmail,
    int? EmployeeCount,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    string Status
);

public record MyWorkspacePayload(
    Guid WorkspaceId,
    Guid TenantId,
    string TenantCode,
    string TenantName,
    Guid MembershipId,
    RoleType Role);

[ExtendObjectType(typeof(global::Query))]
public sealed class AuthQueries
{
    [Authorize]
    public async Task<IReadOnlyList<MyWorkspacePayload>> MyWorkspacesAsync(
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var accountId = GetAuthenticatedAccountId(context);
        var result = await mediator.Send(new GetMyWorkspacesQuery(accountId), cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return result.Value
            .Select(x => new MyWorkspacePayload(
                x.WorkspaceId,
                x.TenantId,
                x.TenantCode,
                x.TenantName,
                x.MembershipId,
                x.Role))
            .ToList();
    }

    [Authorize]
    public async Task<CurrentWorkspacePayload> CurrentWorkspaceAsync(
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var accountId = GetAuthenticatedAccountId(context);
        var tenantId = GetAuthenticatedTenantId(context);
        var membershipId = GetAuthenticatedMembershipId(context);
        var result = await mediator.Send(new GetCurrentWorkspaceQuery(accountId, tenantId, membershipId), cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new CurrentWorkspacePayload(
            result.Value.AccountId,
            result.Value.Email,
            result.Value.MembershipId,
            result.Value.Role,
            result.Value.TenantId,
            result.Value.TenantCode,
            result.Value.TenantName);
    }

    [Authorize]
    public async Task<IReadOnlyList<PendingTenantApprovalPayload>> PendingTenantRequestsAsync(
        [Service] IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetPendingTenantRequestsQuery(), cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return result.Value
            .Select(x => new PendingTenantApprovalPayload(
                x.RequestId,
                x.TenantCode,
                x.Name,
                x.CompanyName,
                x.TaxCode,
                x.RequestedByEmail,
                x.EmployeeCount,
                x.CreatedAt,
                x.ExpiresAt,
                x.Status.ToString()))
            .ToList();
    }

    private static Guid GetAuthenticatedAccountId(IResolverContext context)
    {
        var user = GetUser(context);
        var accountIdClaim = user?.FindFirst("sub")?.Value
            ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(accountIdClaim, out var accountId))
            throw new GraphQLException(new HotChocolate.Error("User is not authenticated or token is invalid", "Account.Unauthorized"));

        return accountId;
    }

    private static Guid GetAuthenticatedTenantId(IResolverContext context)
    {
        var user = GetUser(context);
        var tenantIdClaim = user?.FindFirst("IdTenant")?.Value;

        if (!Guid.TryParse(tenantIdClaim, out var tenantId))
            throw new GraphQLException(new HotChocolate.Error("The current user is not authorized to access this resource.", "Account.Unauthorized"));

        return tenantId;
    }

    private static Guid? GetAuthenticatedMembershipId(IResolverContext context)
    {
        var user = GetUser(context);
        var membershipIdClaim = user?.FindFirst("MembershipId")?.Value;

        return Guid.TryParse(membershipIdClaim, out var membershipId)
            ? membershipId
            : null;
    }

    private static ClaimsPrincipal? GetUser(IResolverContext context)
    {
        var httpContextAccessor = context.Service<IHttpContextAccessor>();
        return httpContextAccessor.HttpContext?.User;
    }
}
