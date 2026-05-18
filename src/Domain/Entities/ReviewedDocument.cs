using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Events;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class ReviewedDocument : Entity, IMultiTenant, ISoftDeletable
{
    private static readonly FinancialErrorFactory ErrorFactory = new(
        DiscountAmountInvalid: ReviewedDocumentErrors.DiscountAmountInvalid,
        DiscountPercentOutOfRange: ReviewedDocumentErrors.DiscountPercentOutOfRange,
        LineItemTotalsMismatch: ReviewedDocumentErrors.LineItemTotalsMismatch,
        DocumentDiscountExceedsSubtotal: ReviewedDocumentErrors.DocumentDiscountExceedsSubtotal,
        DocumentDiscountMismatch: ReviewedDocumentErrors.DocumentDiscountMismatch,
        TotalAmountInvalid: ReviewedDocumentErrors.TotalAmountInvalid,
        FinancialBreakdownMismatch: ReviewedDocumentErrors.FinancialBreakdownMismatch);

    private readonly List<ReviewedDocumentLineItem> _lineItems = [];

    private ReviewedDocument(
        Guid id,
        Guid idTenant,
        Guid idDepartment,
        Guid membershipId,
        string originalFileName,
        string contentType,
        string vendorName,
        string reference,
        DateOnly documentDate,
        string category,
        string? vendorTaxId,
        decimal subtotal,
        decimal? documentDiscountPercent,
        decimal documentDiscountAmount,
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
        IdDepartment = idDepartment;
        MembershipId = membershipId;
        OriginalFileName = originalFileName;
        ContentType = contentType;
        VendorName = vendorName;
        Reference = reference;
        DocumentDate = documentDate;
        Category = category;
        VendorTaxId = vendorTaxId;
        Subtotal = subtotal;
        DocumentDiscountPercent = documentDiscountPercent;
        DocumentDiscountAmount = documentDiscountAmount;
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

    /// <summary>
    /// Apply currency snapshot. Should be called by handler immediately after <c>CreateSubmitted</c>
    /// before saving. After save, currency is immutable.
    /// </summary>
    public Result SetCurrencyContext(string currencyCode, string baseCurrencyCode, decimal exchangeRate)
    {
        var currency = FinFlow.Domain.Common.Currency.Create(currencyCode);
        if (currency.IsFailure) return Result.Failure(currency.Error);

        var baseCurrency = FinFlow.Domain.Common.Currency.Create(baseCurrencyCode);
        if (baseCurrency.IsFailure) return Result.Failure(baseCurrency.Error);

        if (exchangeRate <= 0)
            return Result.Failure(FinFlow.Domain.Common.CurrencyErrors.InvalidRate);

        if (currency.Value.Code == baseCurrency.Value.Code && exchangeRate != 1m)
            return Result.Failure(FinFlow.Domain.Common.CurrencyErrors.MismatchBase);

        CurrencyCode = currency.Value.Code;
        BaseCurrencyCode = baseCurrency.Value.Code;
        ExchangeRate = exchangeRate;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Guid IdTenant { get; private set; }
    public Guid IdDepartment { get; private set; }
    public Guid MembershipId { get; private set; }
    public string OriginalFileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public string VendorName { get; private set; } = null!;
    public string Reference { get; private set; } = null!;
    public DateOnly DocumentDate { get; private set; }
    public string Category { get; private set; } = null!;
    public string? VendorTaxId { get; private set; }
    public Guid? IdVendor { get; private set; }
    public string? DedupHash { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal? DocumentDiscountPercent { get; private set; }
    public decimal DocumentDiscountAmount { get; private set; }
    public decimal Vat { get; private set; }
    public decimal TotalAmount { get; private set; }

    /// <summary>ISO 4217 code of the document's native currency. Defaults to "VND".</summary>
    public string CurrencyCode { get; private set; } = "VND";

    /// <summary>Tenant base currency at the time of submission. Snapshot, never changes after save.</summary>
    public string BaseCurrencyCode { get; private set; } = "VND";

    /// <summary>Rate of <c>1 unit CurrencyCode = ExchangeRate units BaseCurrencyCode</c>. Snapshot.</summary>
    public decimal ExchangeRate { get; private set; } = 1m;

    /// <summary>Convenience: TotalAmount converted to base currency, rounded to 2 decimals.</summary>
    public decimal TotalAmountInBaseCurrency =>
        decimal.Round(TotalAmount * ExchangeRate, 2, MidpointRounding.AwayFromZero);

    public string Source { get; private set; } = null!;
    public string ReviewedByStaff { get; private set; } = null!;
    public string ConfidenceLabel { get; private set; } = null!;
    public ReviewedDocumentStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime SubmittedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public uint Version { get; private set; }
    public bool IsActive { get; private set; } = true;
    public IReadOnlyCollection<ReviewedDocumentLineItem> LineItems => _lineItems.AsReadOnly();

    public static Result<ReviewedDocument> CreateSubmitted(
        Guid documentId,
        Guid idTenant,
        Guid idDepartment,
        Guid membershipId,
        string originalFileName,
        string contentType,
        string vendorName,
        string reference,
        DateOnly documentDate,
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
        => CreateSubmitted(
            documentId,
            idTenant,
            idDepartment,
            membershipId,
            originalFileName,
            contentType,
            vendorName,
            reference,
            documentDate,
            category,
            vendorTaxId,
            subtotal,
            documentDiscountPercent: null,
            documentDiscountAmount: 0m,
            vat,
            totalAmount,
            source,
            reviewedByStaff,
            confidenceLabel,
            submittedAtUtc,
            lineItems);

    public static Result<ReviewedDocument> CreateSubmitted(
        Guid documentId,
        Guid idTenant,
        Guid idDepartment,
        Guid membershipId,
        string originalFileName,
        string contentType,
        string vendorName,
        string reference,
        DateOnly documentDate,
        string category,
        string? vendorTaxId,
        decimal subtotal,
        decimal? documentDiscountPercent,
        decimal documentDiscountAmount,
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
        if (idDepartment == Guid.Empty)
            return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.DepartmentRequired);
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
            if (lineItem.DiscountAmount < 0)
                return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.DiscountAmountInvalid);
            if (lineItem.DiscountPercent.HasValue &&
                (lineItem.DiscountPercent.Value < 0 || lineItem.DiscountPercent.Value > 100))
                return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.DiscountPercentOutOfRange);

            // Cross-check %/amount when both supplied
            if (lineItem.DiscountPercent.HasValue)
            {
                var expectedAmount = FinancialInvariants.RoundMoney(
                    lineItem.Quantity * lineItem.UnitPrice * lineItem.DiscountPercent.Value / 100m);
                if (!FinancialInvariants.EqualsWithinTolerance(expectedAmount, lineItem.DiscountAmount))
                    return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.LineDiscountMismatch);
            }

            // Total = Q*UP - DiscountAmount
            var expectedTotal = FinancialInvariants.RoundMoney(
                lineItem.Quantity * lineItem.UnitPrice - lineItem.DiscountAmount);
            if (!FinancialInvariants.EqualsWithinTolerance(expectedTotal, FinancialInvariants.RoundMoney(lineItem.Total)))
                return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.LineItemTotalInvalid);
        }

        var lineSum = lineItems.Sum(i => i.Total);
        if (!FinancialInvariants.EqualsWithinTolerance(
                FinancialInvariants.RoundMoney(lineSum),
                FinancialInvariants.RoundMoney(totalAmount)))
            return Result.Failure<ReviewedDocument>(ReviewedDocumentErrors.LineItemTotalsMismatch);

        var breakdown = FinancialInvariants.ValidateBreakdownStrict(
            subtotal,
            documentDiscountPercent,
            documentDiscountAmount,
            vat,
            totalAmount,
            ErrorFactory);
        if (breakdown.IsFailure)
            return Result.Failure<ReviewedDocument>(breakdown.Error);

        return Result.Success(new ReviewedDocument(
            documentId,
            idTenant,
            idDepartment,
            membershipId,
            originalFileName.Trim(),
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(),
            vendorName.Trim(),
            reference.Trim(),
            documentDate,
            category.Trim(),
            string.IsNullOrWhiteSpace(vendorTaxId) ? null : vendorTaxId.Trim(),
            subtotal,
            documentDiscountPercent,
            documentDiscountAmount,
            vat,
            totalAmount,
            string.IsNullOrWhiteSpace(source) ? "staff-upload" : source.Trim(),
            reviewedByStaff.Trim(),
            string.IsNullOrWhiteSpace(confidenceLabel) ? "Staff corrected" : confidenceLabel.Trim(),
            submittedAtUtc,
            lineItems));
    }

    public Result Approve(Guid? approvedByMembershipId = null)
    {
        if (Status != ReviewedDocumentStatus.ReadyForApproval)
            return Result.Failure(ReviewedDocumentErrors.AlreadyProcessed);

        Status = ReviewedDocumentStatus.Approved;
        RejectionReason = null;
        UpdatedAt = DateTime.UtcNow;

        RaiseDomainEvent(new ReviewedDocumentApprovedDomainEvent(Id, IdTenant, approvedByMembershipId));
        return Result.Success();
    }

    public Result Reject(string reason, Guid? rejectedByMembershipId = null)
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

        RaiseDomainEvent(new ReviewedDocumentRejectedDomainEvent(Id, IdTenant, rejectedByMembershipId, normalizedReason));
        return Result.Success();
    }

    /// <summary>
    /// Withdraw a submitted document back to Draft state for further editing.
    /// Caller must validate that no Payment exists before invoking (defense-in-depth).
    /// </summary>
    public Result Withdraw()
    {
        if (Status != ReviewedDocumentStatus.ReadyForApproval)
            return Result.Failure(ReviewedDocumentErrors.CannotWithdraw);

        Status = ReviewedDocumentStatus.Draft;
        RejectionReason = null;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ReviewedDocumentWithdrawnDomainEvent(Id, IdTenant, MembershipId));
        return Result.Success();
    }

    /// <summary>
    /// Resubmit a withdrawn document; transitions Draft → ReadyForApproval.
    /// </summary>
    public Result Resubmit(DateTime submittedAtUtc)
    {
        if (Status != ReviewedDocumentStatus.Draft)
            return Result.Failure(ReviewedDocumentErrors.CannotResubmit);

        if (submittedAtUtc.Kind != DateTimeKind.Utc)
            return Result.Failure(ReviewedDocumentErrors.SubmittedAtRequired);

        Status = ReviewedDocumentStatus.ReadyForApproval;
        SubmittedAt = submittedAtUtc;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Replace document fields while in Draft state (after a withdraw, before resubmit).
    /// </summary>
    public Result UpdateForResubmit(
        string vendorName,
        string reference,
        DateOnly documentDate,
        string category,
        string? vendorTaxId,
        decimal subtotal,
        decimal? documentDiscountPercent,
        decimal documentDiscountAmount,
        decimal vat,
        decimal totalAmount,
        string confidenceLabel,
        IReadOnlyCollection<ReviewedDocumentLineItem> lineItems)
    {
        if (Status != ReviewedDocumentStatus.Draft)
            return Result.Failure(ReviewedDocumentErrors.CannotUpdateInCurrentState);

        if (string.IsNullOrWhiteSpace(vendorName))
            return Result.Failure(ReviewedDocumentErrors.VendorNameRequired);
        if (string.IsNullOrWhiteSpace(reference))
            return Result.Failure(ReviewedDocumentErrors.ReferenceRequired);
        if (string.IsNullOrWhiteSpace(category))
            return Result.Failure(ReviewedDocumentErrors.CategoryRequired);
        if (lineItems.Count == 0)
            return Result.Failure(ReviewedDocumentErrors.LineItemRequired);

        var lineSum = lineItems.Sum(i => i.Total);
        if (!FinancialInvariants.EqualsWithinTolerance(
                FinancialInvariants.RoundMoney(lineSum),
                FinancialInvariants.RoundMoney(totalAmount)))
            return Result.Failure(ReviewedDocumentErrors.LineItemTotalsMismatch);

        var breakdown = FinancialInvariants.ValidateBreakdownStrict(
            subtotal,
            documentDiscountPercent,
            documentDiscountAmount,
            vat,
            totalAmount,
            ErrorFactory);
        if (breakdown.IsFailure)
            return breakdown;

        VendorName = vendorName.Trim();
        Reference = reference.Trim();
        DocumentDate = documentDate;
        Category = category.Trim();
        VendorTaxId = string.IsNullOrWhiteSpace(vendorTaxId) ? null : vendorTaxId.Trim();
        Subtotal = subtotal;
        DocumentDiscountPercent = documentDiscountPercent;
        DocumentDiscountAmount = documentDiscountAmount;
        Vat = vat;
        TotalAmount = totalAmount;
        ConfidenceLabel = string.IsNullOrWhiteSpace(confidenceLabel) ? "Staff corrected" : confidenceLabel.Trim();
        UpdatedAt = DateTime.UtcNow;

        _lineItems.Clear();
        _lineItems.AddRange(lineItems);

        return Result.Success();
    }

    /// <summary>
    /// Set or clear the strong link to a Vendor record. Pass null to unlink.
    /// Pure mutator — caller is responsible for ensuring the vendor belongs to
    /// the same tenant. Used by the document submit/save handlers via
    /// <c>IVendorLinkResolver</c> after the lookup-or-create resolution.
    /// </summary>
    public void LinkVendor(Guid? vendorId)
    {
        IdVendor = vendorId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Set the dedup hash used by duplicate-receipt detection. Caller computes
    /// it via <c>DocumentDedupHasher</c>; this method only stores it. Pass
    /// null when the inputs were too sparse for a reliable hash (the caller
    /// then opts out of dedup for this document).
    /// </summary>
    public void SetDedupHash(string? dedupHash)
    {
        DedupHash = string.IsNullOrWhiteSpace(dedupHash) ? null : dedupHash.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Mark the document as a potential duplicate. Soft-warning: doesn't change
    /// status. Raises a domain event so notification mapper can fan out to
    /// accountants/admins.
    /// </summary>
    public void FlagAsPotentialDuplicate(IReadOnlyList<Guid> conflictingDocumentIds)
    {
        if (conflictingDocumentIds.Count == 0)
            return;
        if (string.IsNullOrEmpty(DedupHash))
            return;

        RaiseDomainEvent(new DuplicateReceiptFlaggedDomainEvent(
            DocumentId: Id,
            TenantId: IdTenant,
            DedupHash: DedupHash,
            SubmitterMembershipId: MembershipId,
            ConflictingDocumentIds: conflictingDocumentIds));
    }
}
