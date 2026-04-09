using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public static class TenantErrors
{
    public static readonly Error NotFound = new("Tenant.NotFound", "The tenant with the specified ID was not found");
    public static readonly Error NameRequired = new("Tenant.NameRequired", "Tenant name is required");
    public static readonly Error NameTooLong = new("Tenant.NameTooLong", "Tenant name cannot exceed 150 characters");
    public static readonly Error CodeRequired = new("Tenant.CodeRequired", "Tenant code is required");
    public static readonly Error InvalidCodeFormat = new("Tenant.InvalidCodeFormat", "Tenant code must be 3-50 lowercase alphanumeric characters or hyphens");
    public static readonly Error CodeAlreadyExists = new("Tenant.CodeExists", "A tenant with this code already exists");
    public static readonly Error CodeBlocked = new("Tenant.CodeBlocked", "This tenant code is temporarily unavailable for 30 days after rejection");
    public static readonly Error UserAlreadyHasTenant = new("Tenant.UserAlreadyHasTenant", "User can only create one tenant as owner");
    public static readonly Error Forbidden = new("Tenant.Forbidden", "You do not have permission to create a tenant");
    public static readonly Error InvalidCurrency = new("Tenant.InvalidCurrency", "Currency must be a valid 3-letter ISO code");
    public static readonly Error CompanyNameTooLong = new("Tenant.CompanyNameTooLong", "Company name cannot exceed 150 characters");
    public static readonly Error TaxCodeInvalid = new("Tenant.TaxCodeInvalid", "Tax code must be between 10 and 14 characters");
    public static readonly Error AlreadyDeactivated = new("Tenant.AlreadyDeactivated", "The tenant is already deactivated");
    public static readonly Error AlreadyActive = new("Tenant.AlreadyActive", "The tenant is already active");
}
