using FinFlow.Application.Vendors.Queries.GetVendorByTaxCode;
using FinFlow.Application.Vendors.Queries.GetVendors;
using FinFlow.Domain.Abstractions;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using MediatR;
using Microsoft.AspNetCore.Http;
using DomainError = FinFlow.Domain.Abstractions.Error;

namespace FinFlow.Api.GraphQL.Vendors;

[ExtendObjectType(typeof(global::Query))]
public sealed class VendorQueries
{
    [Authorize]
    public async Task<IReadOnlyList<VendorPayload>> MyVendorsAsync(
        bool? isVerified,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);

        var result = await mediator.Send(
            new GetVendorsQuery(scope.TenantId, isVerified),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return result.Value
            .Select(v => new VendorPayload(
                v.VendorId,
                v.TaxCode,
                v.Name,
                v.IsVerified,
                v.VerifiedByMembershipId,
                v.VerifiedAt,
                v.CreatedAt,
                v.UpdatedAt))
            .ToList();
    }

    [Authorize]
    public async Task<VendorPayload?> VendorByTaxCodeAsync(
        string taxCode,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);

        var result = await mediator.Send(
            new GetVendorByTaxCodeQuery(scope.TenantId, taxCode),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        if (result.Value == null)
            return null;

        return new VendorPayload(
            result.Value.VendorId,
            result.Value.TaxCode,
            result.Value.Name,
            result.Value.IsVerified,
            result.Value.VerifiedByMembershipId,
            result.Value.VerifiedAt,
            result.Value.CreatedAt,
            result.Value.UpdatedAt);
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

public sealed record VendorPayload(
    Guid VendorId,
    string TaxCode,
    string Name,
    bool IsVerified,
    Guid? VerifiedByMembershipId,
    DateTime? VerifiedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);