using FinFlow.Application.Vendors.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Vendors;
using MediatR;

namespace FinFlow.Application.Vendors.Commands.CreateVendor;

public sealed class CreateVendorCommandHandler : IRequestHandler<CreateVendorCommand, Result<Guid>>
{
    private readonly IVendorRepository _vendorRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateVendorCommandHandler(IVendorRepository vendorRepository, IUnitOfWork unitOfWork)
    {
        _vendorRepository = vendorRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateVendorCommand request, CancellationToken cancellationToken)
    {
        var exists = await _vendorRepository.ExistsByTaxCodeAsync(request.TaxCode, request.TenantId, cancellationToken);
        if (exists)
            return Result.Failure<Guid>(VendorErrors.TaxCodeExists);

        var createResult = Vendor.Create(request.TenantId, request.TaxCode, request.Name);
        if (createResult.IsFailure)
            return Result.Failure<Guid>(createResult.Error);

        _vendorRepository.Add(createResult.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(createResult.Value.Id);
    }
}