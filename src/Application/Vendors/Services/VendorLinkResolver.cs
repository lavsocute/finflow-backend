using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Vendors;
using Microsoft.Extensions.Logging;

namespace FinFlow.Application.Vendors.Services;

internal sealed class VendorLinkResolver : IVendorLinkResolver
{
    private readonly IVendorRepository _vendorRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<VendorLinkResolver> _logger;

    public VendorLinkResolver(
        IVendorRepository vendorRepository,
        IUnitOfWork unitOfWork,
        ILogger<VendorLinkResolver> logger)
    {
        _vendorRepository = vendorRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<VendorLinkResult>> ResolveAsync(
        VendorLinkRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.TenantId == Guid.Empty)
            return Result.Failure<VendorLinkResult>(VendorErrors.TenantRequired);

        // No tax code → caller stores free-text VendorName only.
        if (string.IsNullOrWhiteSpace(request.VendorTaxId))
            return Result.Success(VendorLinkResult.NotApplicable);

        var taxCode = request.VendorTaxId.Trim().ToUpperInvariant();

        // Soft validation. We don't reject the document for invalid format here
        // because the field is best-effort metadata in the reimbursement model.
        // We just skip linking. The doc still keeps the snapshot.
        if (!IsTaxCodeShapeValid(taxCode))
        {
            _logger.LogInformation(
                "Skipping vendor link for tenant {TenantId} document {DocumentId}: tax code {TaxCode} fails shape validation.",
                request.TenantId, request.SourceDocumentId, taxCode);
            return Result.Success(VendorLinkResult.NotApplicable);
        }

        // 1. Existing vendor → link.
        var existing = await _vendorRepository.GetEntityByTaxCodeAsync(taxCode, request.TenantId, cancellationToken);
        if (existing is not null)
            return Result.Success(VendorLinkResult.Existing(existing.Id));

        // 2. Auto-create. Vendor.CreateAuto enforces validation + raises
        //    VendorAutoCreatedDomainEvent for audit.
        var createResult = Vendor.CreateAuto(
            request.TenantId,
            taxCode,
            request.VendorName,
            request.CreatedByMembershipId,
            request.SourceDocumentId);

        if (createResult.IsFailure)
        {
            // CreateAuto can still reject (e.g. empty name). Skip the link
            // rather than blocking the whole submit.
            _logger.LogWarning(
                "Skipping vendor auto-create for tenant {TenantId} taxCode {TaxCode}: {Error}",
                request.TenantId, taxCode, createResult.Error.Description);
            return Result.Success(VendorLinkResult.NotApplicable);
        }

        var vendor = createResult.Value;
        _vendorRepository.Add(vendor);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(VendorLinkResult.AutoCreated(vendor.Id));
        }
        catch (Exception ex)
        {
            // Likely a unique-constraint race: another caller inserted same
            // (tenant, tax_code) first. We don't have an EF dependency here so
            // we recover by detaching + re-fetching. If the winner exists,
            // success. If not, this wasn't a race — rethrow.
            _vendorRepository.Detach(vendor);

            var winner = await _vendorRepository.GetEntityByTaxCodeAsync(taxCode, request.TenantId, cancellationToken);
            if (winner is not null)
            {
                _logger.LogInformation(
                    "Vendor auto-create race recovered for tenant {TenantId} taxCode {TaxCode}; linked to existing vendor {VendorId}.",
                    request.TenantId, taxCode, winner.Id);
                return Result.Success(VendorLinkResult.Existing(winner.Id));
            }

            _logger.LogError(ex,
                "Vendor auto-create failed for tenant {TenantId} taxCode {TaxCode}; not a race condition.",
                request.TenantId, taxCode);
            throw;
        }
    }

    private static bool IsTaxCodeShapeValid(string taxCode)
    {
        if (taxCode.Length is < 10 or > 14)
            return false;
        foreach (var c in taxCode)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }
        return true;
    }
}
