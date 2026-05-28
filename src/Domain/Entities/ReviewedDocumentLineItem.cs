namespace FinFlow.Domain.Entities;

public sealed class ReviewedDocumentLineItem
{
    private ReviewedDocumentLineItem(
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

    private ReviewedDocumentLineItem() { }

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

    // Backward-compatible overload (no discount)
    public static ReviewedDocumentLineItem Create(string itemName, decimal quantity, decimal unitPrice, decimal total) =>
        Create(itemName, quantity, unitPrice, discountPercent: null, discountAmount: 0m, total);

    public static ReviewedDocumentLineItem Create(
        string itemName,
        decimal quantity,
        decimal unitPrice,
        decimal? discountPercent,
        decimal discountAmount,
        decimal total,
        decimal? taxRate = null,
        decimal taxableAmount = 0m,
        decimal taxAmount = 0m) =>
        new(Guid.NewGuid(), itemName.Trim(), quantity, unitPrice, discountPercent, discountAmount, taxRate, taxableAmount, taxAmount, total);
}
