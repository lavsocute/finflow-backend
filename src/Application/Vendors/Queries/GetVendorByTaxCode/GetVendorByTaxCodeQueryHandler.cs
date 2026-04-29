using FinFlow.Application.Vendors.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Vendors;
using MediatR;

namespace FinFlow.Application.Vendors.Queries.GetVendorByTaxCode;

public sealed class GetVendorByTaxCodeQueryHandler : IRequestHandler<GetVendorByTaxCodeQuery, Result<VendorResponse?>>
{
    private readonly IVendorRepository _vendorRepository;

    public GetVendorByTaxCodeQueryHandler(IVendorRepository vendorRepository)
    {
        _vendorRepository = vendorRepository;
    }

    public async Task<Result<VendorResponse?>> Handle(GetVendorByTaxCodeQuery request, CancellationToken cancellationToken)
    {
        var vendor = await _vendorRepository.GetByTaxCodeAsync(request.TaxCode, request.TenantId, cancellationToken);

        if (vendor == null)
            return Result.Success<VendorResponse?>(null);

        return Result.Success<VendorResponse?>(new VendorResponse(
            vendor.Id,
            vendor.TaxCode,
            vendor.Name,
            vendor.IsVerified,
            vendor.VerifiedByMembershipId,
            vendor.VerifiedAt,
            vendor.CreatedAt,
            vendor.UpdatedAt));
    }
}