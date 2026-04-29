using FinFlow.Application.Vendors.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Vendors;
using MediatR;

namespace FinFlow.Application.Vendors.Commands.VerifyVendor;

public sealed class VerifyVendorCommandHandler : IRequestHandler<VerifyVendorCommand, Result<VendorResponse>>
{
    private readonly IVendorRepository _vendorRepository;
    private readonly IUnitOfWork _unitOfWork;

    public VerifyVendorCommandHandler(IVendorRepository vendorRepository, IUnitOfWork unitOfWork)
    {
        _vendorRepository = vendorRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<VendorResponse>> Handle(VerifyVendorCommand request, CancellationToken cancellationToken)
    {
        var vendor = await _vendorRepository.GetEntityByIdAsync(request.VendorId, request.TenantId, cancellationToken);
        if (vendor == null)
            return Result.Failure<VendorResponse>(VendorErrors.NotFound);

        var verifyResult = vendor.Verify(request.VerifiedByMembershipId);
        if (verifyResult.IsFailure)
            return Result.Failure<VendorResponse>(verifyResult.Error);

        _vendorRepository.Update(vendor);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new VendorResponse(
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