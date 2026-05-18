using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Vendors.Services;

/// <summary>
/// Resolves a tax code on a document to a vendor's <c>Id</c>, creating the
/// vendor on-the-fly when the tax code is new in the tenant. Returns
/// <see cref="VendorLinkResult.NotApplicable"/> when the tax code is empty or
/// invalid format — caller should leave <c>IdVendor=null</c> in that case
/// (free-text snapshot mode).
///
/// Concurrency-safe: when two staff submit the same new tax code at the same
/// time, the loser of the unique-constraint race re-fetches the winner's
/// vendor and returns its Id. No exception leaks.
/// </summary>
public interface IVendorLinkResolver
{
    Task<Result<VendorLinkResult>> ResolveAsync(
        VendorLinkRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record VendorLinkRequest(
    Guid TenantId,
    string? VendorTaxId,
    string VendorName,
    Guid CreatedByMembershipId,
    Guid SourceDocumentId);

public sealed record VendorLinkResult(Guid? VendorId, bool WasAutoCreated)
{
    /// <summary>Tax code rỗng / invalid format → không link, không reject.</summary>
    public static VendorLinkResult NotApplicable { get; } = new(null, false);

    public static VendorLinkResult Existing(Guid id) => new(id, false);
    public static VendorLinkResult AutoCreated(Guid id) => new(id, true);
}
