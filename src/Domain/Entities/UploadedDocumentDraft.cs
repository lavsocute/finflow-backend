using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class UploadedDocumentDraft : Entity, IMultiTenant
{
    private readonly List<UploadedDocumentDraftLineItem> _lineItems = [];

    private UploadedDocumentDraft(
        Guid id,
        Guid idTenant,
        Guid membershipId,
        string originalFileName,
        string contentType,
        string vendorName,
        string reference,
        DateOnly documentDate,
        DateOnly dueDate,
        string category,
        string? vendorTaxId,
        decimal subtotal,
        decimal vat,
        decimal totalAmount,
        string source,
        string uploadedByStaff,
        string confidenceLabel,
        DateTime uploadedAtUtc,
        IReadOnlyCollection<UploadedDocumentDraftLineItem> lineItems)
    {
        Id = id;
        IdTenant = idTenant;
        MembershipId = membershipId;
        OriginalFileName = originalFileName;
        ContentType = contentType;
        VendorName = vendorName;
        Reference = reference;
        DocumentDate = documentDate;
        DueDate = dueDate;
        Category = category;
        VendorTaxId = vendorTaxId;
        Subtotal = subtotal;
        Vat = vat;
        TotalAmount = totalAmount;
        Source = source;
        UploadedByStaff = uploadedByStaff;
        ConfidenceLabel = confidenceLabel;
        UploadedAt = uploadedAtUtc;
        CreatedAt = uploadedAtUtc;
        UpdatedAt = uploadedAtUtc;
        _lineItems.AddRange(lineItems);
    }

    private UploadedDocumentDraft() { }

    public Guid IdTenant { get; private set; }
    public Guid MembershipId { get; private set; }
    public string OriginalFileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public string VendorName { get; private set; } = null!;
    public string Reference { get; private set; } = null!;
    public DateOnly DocumentDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string Category { get; private set; } = null!;
    public string? VendorTaxId { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal Vat { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string Source { get; private set; } = null!;
    public string UploadedByStaff { get; private set; } = null!;
    public string ConfidenceLabel { get; private set; } = null!;
    public DateTime UploadedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public bool IsActive { get; private set; } = true;
    public IReadOnlyCollection<UploadedDocumentDraftLineItem> LineItems => _lineItems.AsReadOnly();

    public static Result<UploadedDocumentDraft> CreateSuggested(
        Guid documentId,
        Guid idTenant,
        Guid membershipId,
        string originalFileName,
        string contentType,
        string vendorName,
        string reference,
        DateOnly documentDate,
        DateOnly dueDate,
        string category,
        string? vendorTaxId,
        decimal subtotal,
        decimal vat,
        decimal totalAmount,
        string source,
        string uploadedByStaff,
        string confidenceLabel,
        DateTime uploadedAtUtc,
        IReadOnlyCollection<UploadedDocumentDraftLineItem> lineItems)
    {
        if (documentId == Guid.Empty)
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.DocumentIdRequired);
        if (idTenant == Guid.Empty)
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.TenantRequired);
        if (membershipId == Guid.Empty)
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.MembershipRequired);
        if (string.IsNullOrWhiteSpace(originalFileName))
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.FileNameRequired);
        if (string.IsNullOrWhiteSpace(contentType))
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.ContentTypeRequired);
        if (string.IsNullOrWhiteSpace(uploadedByStaff))
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.UploadedByRequired);
        if (string.IsNullOrWhiteSpace(vendorName))
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.VendorNameRequired);
        if (string.IsNullOrWhiteSpace(reference))
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.ReferenceRequired);
        if (string.IsNullOrWhiteSpace(category))
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.CategoryRequired);
        if (totalAmount <= 0)
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.TotalAmountInvalid);
        if (lineItems.Count == 0)
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.LineItemRequired);
        if (uploadedAtUtc.Kind != DateTimeKind.Utc)
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.UploadedAtRequired);

        return Result.Success(new UploadedDocumentDraft(
            documentId,
            idTenant,
            membershipId,
            originalFileName.Trim(),
            contentType.Trim(),
            vendorName.Trim(),
            reference.Trim(),
            documentDate,
            dueDate,
            category.Trim(),
            string.IsNullOrWhiteSpace(vendorTaxId) ? null : vendorTaxId.Trim(),
            subtotal,
            vat,
            totalAmount,
            string.IsNullOrWhiteSpace(source) ? "staff-upload" : source.Trim(),
            uploadedByStaff.Trim(),
            string.IsNullOrWhiteSpace(confidenceLabel) ? "High precision" : confidenceLabel.Trim(),
            uploadedAtUtc,
            lineItems));
    }

    public Result MarkSubmitted()
    {
        if (!IsActive)
            return Result.Failure(UploadedDocumentDraftErrors.NotFound);

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }
}
