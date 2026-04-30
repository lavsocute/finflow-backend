using FinFlow.Api.GraphQL.Auth;
using FinFlow.Application.Vendors.Commands.CreateVendor;
using FinFlow.Application.Vendors.Commands.VerifyVendor;
using FinFlow.Application.Vendors.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using MediatR;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using DomainError = FinFlow.Domain.Abstractions.Error;

namespace FinFlow.Api.GraphQL.Vendors;

public sealed record CreateVendorInput(string TaxCode, string Name);

public sealed record VerifyVendorInput(Guid VendorId);

[ExtendObjectType(typeof(AuthMutations))]
public sealed class VendorMutations
{
    [Authorize]
    public async Task<VendorPayload> CreateVendorAsync(
        CreateVendorInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);

        var result = await mediator.Send(
            new CreateVendorCommand(scope.TenantId, input.TaxCode, input.Name),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        var summary = await mediator.Send(
            new Application.Vendors.Queries.GetVendorByTaxCode.GetVendorByTaxCodeQuery(scope.TenantId, input.TaxCode),
            cancellationToken);

        if (summary.IsFailure)
            throw ToGraphQlException(summary.Error);
        if (summary.Value == null)
            throw ToGraphQlException(new DomainError("Vendor.NotFound", "Vendor not found after creation"));

        return ToVendorPayload(summary.Value);
    }

    [Authorize]
    public async Task<VendorPayload> VerifyVendorAsync(
        VerifyVendorInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var role = EnsureRole(context);
        if (role is not (RoleType.Accountant or RoleType.TenantAdmin))
            throw ToGraphQlException(new DomainError("Vendor.Forbidden", "Only Accountant or Tenant Admin can verify vendors."));

        var scope = EnsureAuthorizedWorkspace(context);

        var result = await mediator.Send(
            new VerifyVendorCommand(input.VendorId, scope.TenantId, scope.MembershipId),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ToVendorPayload(result.Value);
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

    private static VendorPayload ToVendorPayload(VendorResponse response) =>
        new(
            response.VendorId,
            response.TaxCode,
            response.Name,
            response.IsVerified,
            response.VerifiedByMembershipId,
            response.VerifiedAt,
            response.CreatedAt,
            response.UpdatedAt);

    private static GraphQLException ToGraphQlException(DomainError error) =>
        new(new HotChocolate.Error(error.Description, error.Code));
}