using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public static class ReviewedDocumentErrors
{
    public static readonly Error NotFound = new("ReviewedDocument.NotFound", "Reviewed document not found.");
    public static readonly Error Unauthorized = new("ReviewedDocument.Unauthorized", "The current user is not authorized to access this resource.");
    public static readonly Error AlreadyProcessed = new("ReviewedDocument.AlreadyProcessed", "This reviewed document has already been processed.");
    public static readonly Error TenantRequired = new("ReviewedDocument.TenantRequired", "Tenant is required.");
    public static readonly Error MembershipRequired = new("ReviewedDocument.MembershipRequired", "Membership is required.");
    public static readonly Error DocumentIdRequired = new("ReviewedDocument.DocumentIdRequired", "Document id is required.");
    public static readonly Error FileNameRequired = new("ReviewedDocument.FileNameRequired", "Original file name is required.");
    public static readonly Error VendorNameRequired = new("ReviewedDocument.VendorNameRequired", "Vendor name is required.");
    public static readonly Error ReferenceRequired = new("ReviewedDocument.ReferenceRequired", "Reference is required.");
    public static readonly Error CategoryRequired = new("ReviewedDocument.CategoryRequired", "Expense category is required.");
    public static readonly Error ReviewedByRequired = new("ReviewedDocument.ReviewedByRequired", "Reviewed by staff is required.");
    public static readonly Error SubmittedAtRequired = new("ReviewedDocument.SubmittedAtRequired", "Submitted at must be a valid UTC timestamp.");
    public static readonly Error TotalAmountInvalid = new("ReviewedDocument.TotalAmountInvalid", "Total amount must be greater than zero.");
    public static readonly Error LineItemRequired = new("ReviewedDocument.LineItemRequired", "At least one line item is required.");
    public static readonly Error LineItemNameRequired = new("ReviewedDocument.LineItemNameRequired", "Line item name is required.");
    public static readonly Error LineItemQuantityInvalid = new("ReviewedDocument.LineItemQuantityInvalid", "Line item quantity must be greater than zero.");
    public static readonly Error LineItemUnitPriceInvalid = new("ReviewedDocument.LineItemUnitPriceInvalid", "Line item unit price must be greater than zero.");
    public static readonly Error LineItemTotalInvalid = new("ReviewedDocument.LineItemTotalInvalid", "Line item total must be greater than zero.");
    public static readonly Error LineItemTotalsMismatch = new("ReviewedDocument.LineItemTotalsMismatch", "Line item totals must match the reviewed document total amount.");
    public static readonly Error FinancialBreakdownMismatch = new("ReviewedDocument.FinancialBreakdownMismatch", "Subtotal plus tax must match the reviewed document total amount.");
    public static readonly Error ForbiddenApproval = new("ReviewedDocument.ForbiddenApproval", "The current user is not authorized to approve reviewed documents.");
    public static readonly Error SelfApprovalNotAllowed = new("ReviewedDocument.SelfApprovalNotAllowed", "Submitter cannot approve their own reviewed document.");
    public static readonly Error RejectionReasonRequired = new("ReviewedDocument.RejectionReasonRequired", "Rejection reason is required.");
    public static readonly Error RejectionReasonTooLong = new("ReviewedDocument.RejectionReasonTooLong", "Rejection reason cannot exceed 500 characters.");
}
