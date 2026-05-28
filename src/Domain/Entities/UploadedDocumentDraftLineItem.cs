using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public sealed class UploadedDocumentDraftLineItem
{
    private UploadedDocumentDraftLineItem(
        Guid id,
        string itemName,
        decimal quantity,
        decimal unitPrice,
        decimal? discountPercent,
        decimal discountAmount,
        decimal? taxRate,
        decimal taxableAmount,
        decimal taxAmount,
        decimal total)
    {
        Id = id;
        ItemName = itemName;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountPercent = discountPercent;
        DiscountAmount = discountAmount;
        TaxRate = taxRate;
        TaxableAmount = taxableAmount;
        TaxAmount = taxAmount;
        Total = total;
    }

    private UploadedDocumentDraftLineItem() { }

    public Guid Id { get; private set; }
    public string ItemName { get; private set; } = null!;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal? DiscountPercent { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal? TaxRate { get; private set; }
    public decimal TaxableAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal Total { get; private set; }

    public static Result<UploadedDocumentDraftLineItem> Create(
        string itemName,
        decimal quantity,
        decimal unitPrice,
        decimal total)
        => CreateLenient(itemName, quantity, unitPrice, total, null, 0m, 0m);

    public static Result<UploadedDocumentDraftLineItem> CreateLenient(
        string itemName,
        decimal quantity,
        decimal unitPrice,
        decimal total,
        decimal? taxRate = null,
        decimal taxableAmount = 0m,
        decimal taxAmount = 0m)
    {
        // Legacy overload: lenient — does NOT enforce Total = Q*UP formula.
        // OCR may produce rounded/mismatched totals; we accept them as-is.
        if (string.IsNullOrWhiteSpace(itemName))
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineItemNameRequired);

        if (quantity <= 0)
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineItemQuantityInvalid);

        if (unitPrice <= 0)
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineItemUnitPriceInvalid);

        if (total <= 0)
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineItemTotalInvalid);

        if (taxRate.HasValue && (taxRate.Value < 0 || taxRate.Value > 100))
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineItemTotalInvalid);

        if (taxableAmount < 0 || taxAmount < 0)
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineItemTotalInvalid);

        return Result.Success(new UploadedDocumentDraftLineItem(
            Guid.NewGuid(), itemName.Trim(), quantity, unitPrice, null, 0m, taxRate, taxableAmount, taxAmount, total));
    }

    public static Result<UploadedDocumentDraftLineItem> Create(
        string itemName,
        decimal quantity,
        decimal unitPrice,
        decimal? discountPercent,
        decimal discountAmount,
        decimal total,
        decimal? taxRate = null,
        decimal taxableAmount = 0m,
        decimal taxAmount = 0m)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineItemNameRequired);

        if (quantity <= 0)
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineItemQuantityInvalid);

        if (unitPrice <= 0)
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineItemUnitPriceInvalid);

        if (discountAmount < 0)
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.DiscountAmountInvalid);

        if (discountPercent.HasValue && (discountPercent.Value < 0 || discountPercent.Value > 100))
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.DiscountPercentOutOfRange);

        if (taxRate.HasValue && (taxRate.Value < 0 || taxRate.Value > 100))
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineItemTotalInvalid);

        if (taxableAmount < 0 || taxAmount < 0)
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineItemTotalInvalid);

        // Cross-check % vs amount when both supplied
        if (discountPercent.HasValue)
        {
            var expectedAmount = FinancialInvariants.RoundMoney(quantity * unitPrice * discountPercent.Value / 100m);
            if (!FinancialInvariants.EqualsWithinTolerance(expectedAmount, discountAmount))
                return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineDiscountMismatch);
        }

        // Total must equal Q*UP - DiscountAmount within tolerance
        var expectedTotal = FinancialInvariants.RoundMoney(quantity * unitPrice - discountAmount);
        var roundedTotal = FinancialInvariants.RoundMoney(total);
        if (!FinancialInvariants.EqualsWithinTolerance(expectedTotal, roundedTotal))
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineItemTotalInvalid);

        if (roundedTotal < 0)
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineItemTotalInvalid);

        return Result.Success(new UploadedDocumentDraftLineItem(
            Guid.NewGuid(),
            itemName.Trim(),
            quantity,
            unitPrice,
            discountPercent,
            discountAmount,
            taxRate,
            taxableAmount,
            taxAmount,
            total));
    }
}
