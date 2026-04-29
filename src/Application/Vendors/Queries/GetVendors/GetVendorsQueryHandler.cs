using FinFlow.Application.Vendors.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Vendors;
using MediatR;

namespace FinFlow.Application.Vendors.Queries.GetVendors;

public sealed class GetVendorsQueryHandler : IRequestHandler<GetVendorsQuery, Result<IReadOnlyList<VendorResponse>>>
{
    private readonly IVendorRepository _vendorRepository;

    public GetVendorsQueryHandler(IVendorRepository vendorRepository)
    {
        _vendorRepository = vendorRepository;
    }

    public async Task<Result<IReadOnlyList<VendorResponse>>> Handle(GetVendorsQuery request, CancellationToken cancellationToken)
    {
        var vendors = await _vendorRepository.GetAllAsync(request.TenantId, request.IsVerified, cancellationToken);

        var responses = vendors
            .Select(v => new VendorResponse(
                v.Id,
                v.TaxCode,
                v.Name,
                v.IsVerified,
                v.VerifiedByMembershipId,
                v.VerifiedAt,
                v.CreatedAt,
                v.UpdatedAt))
            .ToList();

        return Result.Success<IReadOnlyList<VendorResponse>>(responses);
    }
}