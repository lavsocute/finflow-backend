using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public static class VendorErrors
{
    public static readonly Error NotFound = new("Vendor.NotFound", "The vendor with the specified ID was not found.");
    public static readonly Error TaxCodeRequired = new("Vendor.TaxCodeRequired", "Tax code is required.");
    public static readonly Error TaxCodeInvalid = new("Vendor.TaxCodeInvalid", "Tax code must be between 10 and 14 characters.");
    public static readonly Error TaxCodeExists = new("Vendor.TaxCodeExists", "A vendor with this tax code already exists in the tenant.");
    public static readonly Error NameRequired = new("Vendor.NameRequired", "Vendor name is required.");
    public static readonly Error NameTooLong = new("Vendor.NameTooLong", "Vendor name cannot exceed 200 characters.");
    public static readonly Error AlreadyVerified = new("Vendor.AlreadyVerified", "This vendor has already been verified.");
    public static readonly Error NotVerified = new("Vendor.NotVerified", "This vendor has not been verified.");
    public static readonly Error TenantRequired = new("Vendor.TenantRequired", "Tenant ID is required.");
    public static readonly Error VerifierMembershipIdRequired = new("Vendor.VerifierMembershipIdRequired", "Verifier membership ID is required.");
}