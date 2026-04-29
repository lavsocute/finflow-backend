using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class Vendor : Entity, IMultiTenant
{
    private Vendor(
        Guid id,
        Guid idTenant,
        string taxCode,
        string name,
        bool isVerified,
        Guid? verifiedByMembershipId,
        DateTime? verifiedAt,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        IdTenant = idTenant;
        TaxCode = taxCode;
        Name = name;
        IsVerified = isVerified;
        VerifiedByMembershipId = verifiedByMembershipId;
        VerifiedAt = verifiedAt;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        IsActive = true;
    }

    private Vendor() { }

    public Guid IdTenant { get; private set; }
    public string TaxCode { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public bool IsVerified { get; private set; }
    public Guid? VerifiedByMembershipId { get; private set; }
    public DateTime? VerifiedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<Vendor> Create(
        Guid idTenant,
        string taxCode,
        string name)
    {
        if (idTenant == Guid.Empty)
            return Result.Failure<Vendor>(VendorErrors.TenantRequired);

        var normalizedTaxCode = taxCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedTaxCode))
            return Result.Failure<Vendor>(VendorErrors.TaxCodeRequired);

        var normalizedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
            return Result.Failure<Vendor>(VendorErrors.NameRequired);

        if (normalizedTaxCode.Length is < 10 or > 14)
            return Result.Failure<Vendor>(VendorErrors.TaxCodeInvalid);

        if (!IsValidTaxCodeFormat(normalizedTaxCode))
            return Result.Failure<Vendor>(VendorErrors.TaxCodeInvalid);

        if (normalizedName.Length > 200)
            return Result.Failure<Vendor>(VendorErrors.NameTooLong);

        var now = DateTime.UtcNow;
        return Result.Success(new Vendor(
            Guid.NewGuid(),
            idTenant,
            normalizedTaxCode.ToUpperInvariant(),
            normalizedName,
            isVerified: false,
            verifiedByMembershipId: null,
            verifiedAt: null,
            createdAt: now,
            updatedAt: now));
    }

    private static bool IsValidTaxCodeFormat(string taxCode)
    {
        if (string.IsNullOrWhiteSpace(taxCode))
            return false;

        foreach (var c in taxCode)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return true;
    }

    public Result Verify(Guid verifiedByMembershipId)
    {
        if (IsVerified)
            return Result.Failure(VendorErrors.AlreadyVerified);

        if (verifiedByMembershipId == Guid.Empty)
            return Result.Failure(VendorErrors.VerifierMembershipIdRequired);

        IsVerified = true;
        VerifiedByMembershipId = verifiedByMembershipId;
        VerifiedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Unverify()
    {
        if (!IsVerified)
            return Result.Failure(VendorErrors.NotVerified);

        IsVerified = false;
        VerifiedByMembershipId = null;
        VerifiedAt = null;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result UpdateInfo(string name)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
            return Result.Failure(VendorErrors.NameRequired);

        if (normalizedName.Length > 200)
            return Result.Failure(VendorErrors.NameTooLong);

        Name = normalizedName;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Result.Success();

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Activate()
    {
        if (IsActive)
            return Result.Success();

        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }
}