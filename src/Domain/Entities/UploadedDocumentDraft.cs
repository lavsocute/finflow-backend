using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Events;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class UploadedDocumentDraft : Entity, IMultiTenant, ISoftDeletable
{
    private static readonly FinancialErrorFactory ErrorFactory = new(
        DiscountAmountInvalid: UploadedDocumentDraftErrors.DiscountAmountInvalid,
        DiscountPercentOutOfRange: UploadedDocumentDraftErrors.DiscountPercentOutOfRange,
        LineItemTotalsMismatch: UploadedDocumentDraftErrors.LineItemTotalsMismatch,
        DocumentDiscountExceedsSubtotal: UploadedDocumentDraftErrors.DocumentDiscountExceedsSubtotal,
        DocumentDiscountMismatch: UploadedDocumentDraftErrors.DocumentDiscountMismatch,
        TotalAmountInvalid: UploadedDocumentDraftErrors.TotalAmountInvalid,
        FinancialBreakdownMismatch: UploadedDocumentDraftErrors.FinancialBreakdownMismatch);

    private readonly List<UploadedDocumentDraftLineItem> _lineItems = [];
    private readonly List<UploadedDocumentDraftTaxLine> _taxLines = [];

    private UploadedDocumentDraft(
        Guid id,
        Guid idTenant,
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
        string uploadedByStaff,
        string confidenceLabel,
        DateTime uploadedAtUtc,
        string? imageContentType,
        byte[]? imageData,
        IReadOnlyCollection<UploadedDocumentDraftLineItem> lineItems,
        IReadOnlyCollection<UploadedDocumentDraftTaxLine> taxLines)
    {
        Id = id;
        IdTenant = idTenant;
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
        UploadedByStaff = uploadedByStaff;
        ConfidenceLabel = confidenceLabel;
        UploadedAt = uploadedAtUtc;
        CreatedAt = uploadedAtUtc;
        UpdatedAt = uploadedAtUtc;
        ImageContentType = imageContentType;
        ImageData = imageData;
        _lineItems.AddRange(lineItems);
        _taxLines.AddRange(taxLines);
    }

    private UploadedDocumentDraft() { }

    /// <summary>
    /// Apply currency snapshot. Called by handler when creating draft from OCR or manual entry.
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
    public Guid MembershipId { get; private set; }
    public string OriginalFileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public string VendorName { get; private set; } = null!;
    public string Reference { get; private set; } = null!;
    public DateOnly DocumentDate { get; private set; }
    public string Category { get; private set; } = null!;
    public string? VendorTaxId { get; private set; }
    public Guid? IdVendor { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal? DocumentDiscountPercent { get; private set; }
    public decimal DocumentDiscountAmount { get; private set; }
    public decimal Vat { get; private set; }
    public decimal TotalAmount { get; private set; }

    /// <summary>ISO 4217 code of the document's native currency. Defaults to "VND".</summary>
    public string CurrencyCode { get; private set; } = "VND";

    /// <summary>Tenant base currency at the time of upload. Snapshot.</summary>
    public string BaseCurrencyCode { get; private set; } = "VND";

    /// <summary>Rate of 1 unit CurrencyCode = ExchangeRate units BaseCurrencyCode. Snapshot.</summary>
    public decimal ExchangeRate { get; private set; } = 1m;

    public string Source { get; private set; } = null!;
    public string UploadedByStaff { get; private set; } = null!;
    public string ConfidenceLabel { get; private set; } = null!;
    public DateTime UploadedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public uint Version { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool HasImage { get => ImageData is { Length: > 0 }; }
    public string? ImageContentType { get; private set; }
    public byte[]? ImageData { get; private set; }
    public IReadOnlyCollection<UploadedDocumentDraftLineItem> LineItems => _lineItems.AsReadOnly();
    public IReadOnlyCollection<UploadedDocumentDraftTaxLine> TaxLines => _taxLines.AsReadOnly();

    public static Result<UploadedDocumentDraft> CreateSuggested(
        Guid documentId,
        Guid idTenant,
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
        string uploadedByStaff,
        string confidenceLabel,
        DateTime uploadedAtUtc,
        string? imageContentType,
        byte[]? imageData,
        IReadOnlyCollection<UploadedDocumentDraftLineItem> lineItems,
        IReadOnlyCollection<UploadedDocumentDraftTaxLine>? taxLines = null)
        => CreateSuggested(
            documentId,
            idTenant,
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
            uploadedByStaff,
            confidenceLabel,
            uploadedAtUtc,
            imageContentType,
            imageData,
            lineItems,
            taxLines);

    public static Result<UploadedDocumentDraft> CreateSuggested(
        Guid documentId,
        Guid idTenant,
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
        string uploadedByStaff,
        string confidenceLabel,
        DateTime uploadedAtUtc,
        string? imageContentType,
        byte[]? imageData,
        IReadOnlyCollection<UploadedDocumentDraftLineItem> lineItems,
        IReadOnlyCollection<UploadedDocumentDraftTaxLine>? taxLines = null)
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
        if (lineItems.Count == 0)
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.LineItemRequired);
        if (uploadedAtUtc.Kind != DateTimeKind.Utc)
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.UploadedAtRequired);
        if (imageData is { Length: > 0 } && string.IsNullOrWhiteSpace(imageContentType))
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.ImageContentTypeRequired);

        var breakdown = FinancialInvariants.ValidateBreakdown(
            subtotal,
            documentDiscountPercent,
            documentDiscountAmount,
            vat,
            totalAmount,
            ErrorFactory);
        if (breakdown.IsFailure)
            return Result.Failure<UploadedDocumentDraft>(breakdown.Error);

        var normalizedTaxLines = NormalizeTaxLines(taxLines, subtotal, vat);
        if (normalizedTaxLines.IsFailure)
            return Result.Failure<UploadedDocumentDraft>(normalizedTaxLines.Error);

        return Result.Success(new UploadedDocumentDraft(
            documentId,
            idTenant,
            membershipId,
            originalFileName.Trim(),
            contentType.Trim(),
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
            uploadedByStaff.Trim(),
            string.IsNullOrWhiteSpace(confidenceLabel) ? "High precision" : confidenceLabel.Trim(),
            uploadedAtUtc,
            imageContentType?.Trim(),
            imageData,
            lineItems,
            normalizedTaxLines.Value));
    }

    public static Result<UploadedDocumentDraft> CreateManual(
        Guid idTenant,
        Guid membershipId,
        string originalFileName,
        string vendorName,
        string reference,
        DateOnly documentDate,
        string category,
        string? vendorTaxId,
        decimal subtotal,
        decimal vat,
        decimal totalAmount,
        string uploadedByStaff,
        IReadOnlyCollection<UploadedDocumentDraftLineItem> lineItems,
        IReadOnlyCollection<UploadedDocumentDraftTaxLine>? taxLines = null)
        => CreateManual(
            idTenant,
            membershipId,
            originalFileName,
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
            uploadedByStaff,
            lineItems,
            taxLines);

    public static Result<UploadedDocumentDraft> CreateManual(
        Guid idTenant,
        Guid membershipId,
        string originalFileName,
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
        string uploadedByStaff,
        IReadOnlyCollection<UploadedDocumentDraftLineItem> lineItems,
        IReadOnlyCollection<UploadedDocumentDraftTaxLine>? taxLines = null)
    {
        if (idTenant == Guid.Empty)
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.TenantRequired);
        if (membershipId == Guid.Empty)
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.MembershipRequired);
        if (string.IsNullOrWhiteSpace(vendorName))
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.VendorNameRequired);
        if (string.IsNullOrWhiteSpace(reference))
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.ReferenceRequired);
        if (string.IsNullOrWhiteSpace(category))
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.CategoryRequired);
        if (lineItems.Count == 0)
            return Result.Failure<UploadedDocumentDraft>(UploadedDocumentDraftErrors.LineItemRequired);

        var breakdown = FinancialInvariants.ValidateBreakdown(
            subtotal,
            documentDiscountPercent,
            documentDiscountAmount,
            vat,
            totalAmount,
            ErrorFactory);
        if (breakdown.IsFailure)
            return Result.Failure<UploadedDocumentDraft>(breakdown.Error);

        var normalizedTaxLines = NormalizeTaxLines(taxLines, subtotal, vat);
        if (normalizedTaxLines.IsFailure)
            return Result.Failure<UploadedDocumentDraft>(normalizedTaxLines.Error);

        var now = DateTime.UtcNow;
        return Result.Success(new UploadedDocumentDraft(
            Guid.NewGuid(),
            idTenant,
            membershipId,
            string.IsNullOrWhiteSpace(originalFileName) ? "manual-entry" : originalFileName.Trim(),
            "manual-entry",
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
            "manual-entry",
            uploadedByStaff.Trim(),
            "Manual entry",
            now,
            null,
            null,
            lineItems,
            normalizedTaxLines.Value));
    }

    public Result MarkSubmitted()
    {
        if (!IsActive)
            return Result.Failure(UploadedDocumentDraftErrors.NotFound);

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Soft-delete an active draft. Preserves audit chain; sets IsActive=false and DeletedAt.
    /// </summary>
    public Result SoftDelete()
    {
        if (!IsActive)
            return Result.Failure(UploadedDocumentDraftErrors.AlreadySubmitted);

        IsActive = false;
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new UploadedDocumentDraftDeletedDomainEvent(Id, IdTenant, MembershipId));
        return Result.Success();
    }

    /// <summary>
    /// Reactivate this draft from a snapshot of the corresponding ReviewedDocument
    /// (used when a submission is withdrawn).
    /// </summary>
    public Result ReactivateFromSnapshot(ReviewedDocument document)
    {
        if (document is null)
            throw new ArgumentNullException(nameof(document));

        if (document.Id != Id || document.IdTenant != IdTenant)
            return Result.Failure(UploadedDocumentDraftErrors.NotFound);

        if (IsActive)
            return Result.Failure(UploadedDocumentDraftErrors.AlreadyActive);

        IsActive = true;
        DeletedAt = null;
        VendorName = document.VendorName;
        Reference = document.Reference;
        DocumentDate = document.DocumentDate;
        Category = document.Category;
        VendorTaxId = document.VendorTaxId;
        Subtotal = document.Subtotal;
        DocumentDiscountPercent = document.DocumentDiscountPercent;
        DocumentDiscountAmount = document.DocumentDiscountAmount;
        Vat = document.Vat;
        TotalAmount = document.TotalAmount;
        ConfidenceLabel = "Withdrawn for edit";
        UpdatedAt = DateTime.UtcNow;

        _lineItems.Clear();
        foreach (var li in document.LineItems)
        {
            var copyResult = UploadedDocumentDraftLineItem.Create(
                li.ItemName, li.Quantity, li.UnitPrice, li.DiscountPercent, li.DiscountAmount, li.Total);
            if (copyResult.IsFailure)
                return Result.Failure(copyResult.Error);
            _lineItems.Add(copyResult.Value);
        }

        _taxLines.Clear();
        foreach (var taxLine in document.TaxLines)
        {
            var copyResult = UploadedDocumentDraftTaxLine.Create(
                taxLine.TaxType,
                taxLine.Rate,
                taxLine.TaxableAmount,
                taxLine.TaxAmount);
            if (copyResult.IsFailure)
                return Result.Failure(copyResult.Error);
            _taxLines.Add(copyResult.Value);
        }

        return Result.Success();
    }

    /// <summary>
    /// Build a fresh draft from a withdrawn submission when no draft existed (manual submission case).
    /// </summary>
    public static Result<UploadedDocumentDraft> CreateFromSnapshot(ReviewedDocument document, string uploadedByStaff)
    {
        if (document is null)
            throw new ArgumentNullException(nameof(document));

        var lineItems = new List<UploadedDocumentDraftLineItem>();
        foreach (var li in document.LineItems)
        {
            var copy = UploadedDocumentDraftLineItem.Create(
                li.ItemName, li.Quantity, li.UnitPrice, li.DiscountPercent, li.DiscountAmount, li.Total);
            if (copy.IsFailure)
                return Result.Failure<UploadedDocumentDraft>(copy.Error);
            lineItems.Add(copy.Value);
        }

        var now = DateTime.UtcNow;
        return Result.Success(new UploadedDocumentDraft(
            document.Id,
            document.IdTenant,
            document.MembershipId,
            document.OriginalFileName,
            string.IsNullOrWhiteSpace(document.ContentType) ? "manual-entry" : document.ContentType,
            document.VendorName,
            document.Reference,
            document.DocumentDate,
            document.Category,
            document.VendorTaxId,
            document.Subtotal,
            document.DocumentDiscountPercent,
            document.DocumentDiscountAmount,
            document.Vat,
            document.TotalAmount,
            "withdrawn-for-edit",
            string.IsNullOrWhiteSpace(uploadedByStaff) ? document.ReviewedByStaff : uploadedByStaff.Trim(),
            "Withdrawn for edit",
            now,
            null,
            null,
            lineItems,
            CopyReviewedTaxLines(document.TaxLines)));
    }

    public Result UpdateReviewedData(
        string vendorName,
        string reference,
        DateOnly documentDate,
        string category,
        string? vendorTaxId,
        decimal subtotal,
        decimal vat,
        decimal totalAmount,
        string confidenceLabel,
        IReadOnlyCollection<UploadedDocumentDraftLineItem> lineItems,
        IReadOnlyCollection<UploadedDocumentDraftTaxLine>? taxLines = null)
        => UpdateDraftFields(
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
            confidenceLabel,
            lineItems,
            taxLines);

    public Result UpdateDraftFields(
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
        IReadOnlyCollection<UploadedDocumentDraftLineItem> lineItems,
        IReadOnlyCollection<UploadedDocumentDraftTaxLine>? taxLines = null)
    {
        if (!IsActive)
            return Result.Failure(UploadedDocumentDraftErrors.AlreadySubmitted);

        if (string.IsNullOrWhiteSpace(vendorName))
            return Result.Failure(UploadedDocumentDraftErrors.VendorNameRequired);
        if (string.IsNullOrWhiteSpace(reference))
            return Result.Failure(UploadedDocumentDraftErrors.ReferenceRequired);
        if (string.IsNullOrWhiteSpace(category))
            return Result.Failure(UploadedDocumentDraftErrors.CategoryRequired);
        if (lineItems.Count == 0)
            return Result.Failure(UploadedDocumentDraftErrors.LineItemRequired);

        var breakdown = FinancialInvariants.ValidateBreakdown(
            subtotal,
            documentDiscountPercent,
            documentDiscountAmount,
            vat,
            totalAmount,
            ErrorFactory);
        if (breakdown.IsFailure)
            return breakdown;

        var normalizedTaxLines = NormalizeTaxLines(taxLines, subtotal, vat);
        if (normalizedTaxLines.IsFailure)
            return normalizedTaxLines;

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
        _taxLines.Clear();
        _taxLines.AddRange(normalizedTaxLines.Value);

        return Result.Success();
    }

    /// <summary>
    /// Set or clear the strong link to a Vendor record. Pure mutator; caller
    /// is responsible for tenant scoping.
    /// </summary>
    public void LinkVendor(Guid? vendorId)
    {
        IdVendor = vendorId;
        UpdatedAt = DateTime.UtcNow;
    }

    private static Result<IReadOnlyCollection<UploadedDocumentDraftTaxLine>> NormalizeTaxLines(
        IReadOnlyCollection<UploadedDocumentDraftTaxLine>? taxLines,
        decimal taxableAmount,
        decimal vat)
    {
        if (taxLines is null || taxLines.Count == 0)
            return Result.Success<IReadOnlyCollection<UploadedDocumentDraftTaxLine>>(CreateFallbackTaxLines(taxableAmount, vat));

        var taxAmount = taxLines.Sum(x => x.TaxAmount);
        if (!FinancialInvariants.EqualsWithinTolerance(
                FinancialInvariants.RoundMoney(taxAmount),
                FinancialInvariants.RoundMoney(vat)))
            return Result.Failure<IReadOnlyCollection<UploadedDocumentDraftTaxLine>>(UploadedDocumentDraftErrors.FinancialBreakdownMismatch);

        return Result.Success<IReadOnlyCollection<UploadedDocumentDraftTaxLine>>(taxLines.ToList());
    }

    private static IReadOnlyCollection<UploadedDocumentDraftTaxLine> CreateFallbackTaxLines(decimal taxableAmount, decimal vat)
    {
        if (vat <= 0)
            return [];

        return
        [
            UploadedDocumentDraftTaxLine.Create("VAT", null, taxableAmount, vat).Value
        ];
    }

    private static IReadOnlyCollection<UploadedDocumentDraftTaxLine> CopyReviewedTaxLines(
        IReadOnlyCollection<ReviewedDocumentTaxLine> taxLines)
    {
        return taxLines
            .Select(x => UploadedDocumentDraftTaxLine.Create(x.TaxType, x.Rate, x.TaxableAmount, x.TaxAmount).Value)
            .ToList();
    }
}
