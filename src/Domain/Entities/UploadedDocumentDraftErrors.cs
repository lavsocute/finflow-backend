using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public static class UploadedDocumentDraftErrors
{
    public static readonly Error NotFound = new("UploadedDocumentDraft.NotFound", "Uploaded document draft not found.");
    public static readonly Error Unauthorized = new("UploadedDocumentDraft.Unauthorized", "The current user is not authorized to access this resource.");
    public static readonly Error TenantRequired = new("UploadedDocumentDraft.TenantRequired", "Tenant is required.");
    public static readonly Error MembershipRequired = new("UploadedDocumentDraft.MembershipRequired", "Membership is required.");
    public static readonly Error DocumentIdRequired = new("UploadedDocumentDraft.DocumentIdRequired", "Document id is required.");
    public static readonly Error FileNameRequired = new("UploadedDocumentDraft.FileNameRequired", "Original file name is required.");
    public static readonly Error ContentTypeRequired = new("UploadedDocumentDraft.ContentTypeRequired", "Content type is required.");
    public static readonly Error UploadedByRequired = new("UploadedDocumentDraft.UploadedByRequired", "Uploaded by staff is required.");
    public static readonly Error VendorNameRequired = new("UploadedDocumentDraft.VendorNameRequired", "Vendor name is required.");
    public static readonly Error ReferenceRequired = new("UploadedDocumentDraft.ReferenceRequired", "Reference is required.");
    public static readonly Error CategoryRequired = new("UploadedDocumentDraft.CategoryRequired", "Expense category is required.");
    public static readonly Error TotalAmountInvalid = new("UploadedDocumentDraft.TotalAmountInvalid", "Total amount must be greater than zero.");
    public static readonly Error LineItemRequired = new("UploadedDocumentDraft.LineItemRequired", "At least one line item is required.");
    public static readonly Error LineItemNameRequired = new("UploadedDocumentDraft.LineItemNameRequired", "Line item name is required.");
    public static readonly Error LineItemQuantityInvalid = new("UploadedDocumentDraft.LineItemQuantityInvalid", "Line item quantity must be greater than zero.");
    public static readonly Error LineItemUnitPriceInvalid = new("UploadedDocumentDraft.LineItemUnitPriceInvalid", "Line item unit price must be greater than zero.");
    public static readonly Error LineItemTotalInvalid = new("UploadedDocumentDraft.LineItemTotalInvalid", "Line item total must be greater than zero.");
    public static readonly Error LineItemTotalsMismatch = new("UploadedDocumentDraft.LineItemTotalsMismatch", "Line item totals must match the uploaded document total amount.");
    public static readonly Error FinancialBreakdownMismatch = new("UploadedDocumentDraft.FinancialBreakdownMismatch", "Subtotal plus tax must match the uploaded document total amount.");
    public static readonly Error UploadedAtRequired = new("UploadedDocumentDraft.UploadedAtRequired", "Uploaded at must be a valid UTC timestamp.");
public static readonly Error UnsupportedContentType = new("UploadedDocumentDraft.UnsupportedContentType", "Only PDF and image uploads are supported.");
    public static readonly Error FileTooLarge = new("UploadedDocumentDraft.FileTooLarge", "The uploaded file exceeds the maximum allowed size of 10MB.");
    public static readonly Error OcrNotAvailableForCurrentPlan = new("Documents.OcrNotAvailableForCurrentPlan", "OCR is not available for the current plan.");
    public static readonly Error ImageContentTypeRequired = new("UploadedDocumentDraft.ImageContentTypeRequired", "Image content type is required when has image is true.");
}
