using FinFlow.Application.Vendors.Queries.GetVendorByTaxCode;
using FinFlow.Application.Vendors.Queries.GetVendors;
using FinFlow.Application.Vendors.Services;
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
    public async Task<IReadOnlyList<VendorListItemPayload>> MyVendorsAsync(
        bool? isVerified,
        [Service] IMediator mediator,
        [Service] IVendorWorkspaceReadService vendorWorkspaceReadService,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);

        var result = await mediator.Send(
            new GetVendorsQuery(scope.TenantId, isVerified),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        var linkedDocumentCounts = await vendorWorkspaceReadService.GetLinkedDocumentCountsAsync(
            scope.TenantId,
            result.Value.Select(vendor => vendor.VendorId).ToArray(),
            cancellationToken);

        return result.Value
            .Select(v => new VendorListItemPayload(
                v.VendorId,
                v.TaxCode,
                v.Name,
                v.IsVerified,
                v.VerifiedByMembershipId,
                v.VerifiedAt,
                v.CreatedAt,
                v.UpdatedAt,
                linkedDocumentCounts.GetValueOrDefault(v.VendorId)))
            .ToList();
    }

    [Authorize]
    public async Task<VendorDetailPayload> VendorDetailAsync(
        Guid vendorId,
        [Service] IVendorWorkspaceReadService vendorWorkspaceReadService,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);
        var detail = await vendorWorkspaceReadService.GetDetailAsync(
            scope.TenantId,
            vendorId,
            cancellationToken);

        if (detail is null)
            throw ToGraphQlException(new DomainError("Vendor.NotFound", "Vendor not found."));

        return new VendorDetailPayload(
            detail.VendorId,
            detail.LinkedDocumentsCount,
            detail.RecentDocuments
                .Select(document => new VendorLinkedDocumentPayload(
                    document.DocumentId,
                    document.Reference,
                    document.Category,
                    document.Status,
                    document.TotalAmount,
                    document.CurrencyCode,
                    document.DocumentDate))
                .ToList());
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

public sealed record VendorListItemPayload(
    Guid VendorId,
    string TaxCode,
    string Name,
    bool IsVerified,
    Guid? VerifiedByMembershipId,
    DateTime? VerifiedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int LinkedDocumentsCount);

public sealed record VendorDetailPayload(
    Guid VendorId,
    int LinkedDocumentsCount,
    IReadOnlyList<VendorLinkedDocumentPayload> RecentDocuments);

public sealed record VendorLinkedDocumentPayload(
    Guid DocumentId,
    string Reference,
    string Category,
    string Status,
    decimal TotalAmount,
    string CurrencyCode,
    DateOnly DocumentDate);
