using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public static class TenantApprovalRequestErrors
{
    public static readonly Error NotFound = new("TenantApproval.NotFound", "Tenant approval request not found");
    public static readonly Error TenantCodeRequired = new("TenantApproval.TenantCodeRequired", "Tenant code is required");
    public static readonly Error InvalidTenantCodeFormat = new("TenantApproval.InvalidTenantCodeFormat", "Tenant code must be 3-50 lowercase alphanumeric characters or hyphens");
    public static readonly Error NameRequired = new("TenantApproval.NameRequired", "Tenant name is required");
    public static readonly Error NameTooLong = new("TenantApproval.NameTooLong", "Tenant name cannot exceed 150 characters");
    public static readonly Error CompanyInfoRequired = new("TenantApproval.CompanyInfoRequired", "Company information is required for isolated tenant requests");
    public static readonly Error CompanyNameTooLong = new("TenantApproval.CompanyNameTooLong", "Company name cannot exceed 150 characters");
    public static readonly Error TaxCodeInvalid = new("TenantApproval.TaxCodeInvalid", "Tax code must be between 10 and 14 characters");
    public static readonly Error AddressTooLong = new("TenantApproval.AddressTooLong", "Address cannot exceed 500 characters");
    public static readonly Error PhoneTooLong = new("TenantApproval.PhoneTooLong", "Phone cannot exceed 15 characters");
    public static readonly Error ContactPersonTooLong = new("TenantApproval.ContactPersonTooLong", "Contact person cannot exceed 100 characters");
    public static readonly Error BusinessTypeTooLong = new("TenantApproval.BusinessTypeTooLong", "Business type cannot exceed 50 characters");
    public static readonly Error InvalidEmployeeCount = new("TenantApproval.InvalidEmployeeCount", "Employee count must be greater than zero");
    public static readonly Error InvalidCurrency = new("TenantApproval.InvalidCurrency", "Currency must be a valid 3-letter ISO code");
    public static readonly Error RequestedByRequired = new("TenantApproval.RequestedByRequired", "RequestedBy is required");
    public static readonly Error ExpirationRequired = new("TenantApproval.ExpirationRequired", "Expiration must be in the future");
    public static readonly Error AlreadyProcessed = new("TenantApproval.AlreadyProcessed", "This request has already been processed");
    public static readonly Error Unauthorized = new("TenantApproval.Unauthorized", "Only SuperAdmin can perform this action");
    public static readonly Error RejectionReasonRequired = new("TenantApproval.RejectionReasonRequired", "Rejection reason is required");
    public static readonly Error RejectionReasonTooLong = new("TenantApproval.RejectionReasonTooLong", "Rejection reason cannot exceed 500 characters");
    public static readonly Error Expired = new("TenantApproval.Expired", "This request has expired");
}
