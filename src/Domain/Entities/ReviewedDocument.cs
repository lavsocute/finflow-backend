using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class ReviewedDocument : Entity, IMultiTenant
{
    private readonly List<ReviewedDocumentLineItem> _lineItems = [];

    private ReviewedDocument(
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
        string reviewedByStaff,
        string confidenceLabel,
        DateTime submittedAtUtc,
        IReadOnlyCollection<ReviewedDocumentLineItem> lineItems)
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
        ReviewedByStaff = reviewedByStaff;
        ConfidenceLabel = confidenceLabel;
        Status = ReviewedDocumentStatus.ReadyForApproval;
        RejectionReason = null;
        SubmittedAt = submittedAtUtc;
        CreatedAt = submittedAtUtc;
        UpdatedAt = submittedAtUtc;
        _lineItems.AddRange(lineItems);
    }

    private ReviewedDocument() { }

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
    public string ReviewedByStaff { get; private set; } = null!;
    public string ConfidenceLabel { get; private set; } = null!;
    public ReviewedDocumentStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime SubmittedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public bool IsActive { get; private set; } = true;
    public IReadOnlyCollection<ReviewedDocumentLineItem> LineItems => _lineItems.AsReadOnly();

    public static Result<ReviewedDocument> CreateSubmitted(
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
        string reviewedByStaff,
        string confidenceLabel,
        DateTime submittedAtUtc,
        IReadOnlyCollection<ReviewedDocumentLineItem> lineItems)
    {
        if (documentId == Guid.Empty)
            return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.DocumentIdRequired);
        if (idTenant == Guid.Empty)
            return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.TenantRequired);
        if (membershipId == Guid.Empty)
            return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.MembershipRequired);
        if (string.IsNullOrWhiteSpace(originalFileName))
            return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.FileNameRequired);
        if (string.IsNullOrWhiteSpace(vendorName))
            return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.VendorNameRequired);
        if (string.IsNullOrWhiteSpace(reference))
            return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.ReferenceRequired);
        if (string.IsNullOrWhiteSpace(category))
            return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.CategoryRequired);
        if (string.IsNullOrWhiteSpace(reviewedByStaff))
            return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.ReviewedByRequired);
        if (submittedAtUtc.Kind != DateTimeKind.Utc)
            return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.SubmittedAtRequired);
        if (totalAmount <= 0)
            return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.TotalAmountInvalid);
        if (lineItems.Count == 0)
            return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.LineItemRequired);

        foreach (var lineItem in lineItems)
        {
            if (string.IsNullOrWhiteSpace(lineItem.ItemName))
                return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.LineItemNameRequired);
            if (lineItem.Quantity <= 0)
                return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.LineItemQuantityInvalid);
            if (lineItem.UnitPrice <= 0)
                return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.LineItemUnitPriceInvalid);
            if (lineItem.Total <= 0)
                return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.LineItemTotalInvalid);
        }

        var roundedTotalAmount = decimal.Round(totalAmount, 2, MidpointRounding.AwayFromZero);
        var roundedLineItemTotal = decimal.Round(lineItems.Sum(item => item.Total), 2, MidpointRounding.AwayFromZero);
        if (roundedLineItemTotal != roundedTotalAmount)
            return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.LineItemTotalsMismatch);

        var roundedBreakdownTotal = decimal.Round(subtotal + vat, 2, MidpointRounding.AwayFromZero);
        if (roundedBreakdownTotal != roundedTotalAmount)
            return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.FinancialBreakdownMismatch);

        return Result.Success(new ReviewedDocument(
            documentId,
            idTenant,
            membershipId,
            originalFileName.Trim(),
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(),
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
            reviewedByStaff.Trim(),
            string.IsNullOrWhiteSpace(confidenceLabel) ? "Staff corrected" : confidenceLabel.Trim(),
            submittedAtUtc,
            lineItems));
    }

    public Result Approve()
    {
        if (Status != ReviewedDocumentStatus.ReadyForApproval)
            return Result.Failure(ReviewedDocumentErrors.AlreadyProcessed);

        Status = ReviewedDocumentStatus.Approved;
        RejectionReason = null;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Reject(string reason)
    {
        if (Status != ReviewedDocumentStatus.ReadyForApproval)
            return Result.Failure(ReviewedDocumentErrors.AlreadyProcessed);

        var normalizedReason = reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedReason))
            return Result.Failure(ReviewedDocumentErrors.RejectionReasonRequired);
        if (normalizedReason.Length > 500)
            return Result.Failure(ReviewedDocumentErrors.RejectionReasonTooLong);

        Status = ReviewedDocumentStatus.Rejected;
        RejectionReason = normalizedReason;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }
}
