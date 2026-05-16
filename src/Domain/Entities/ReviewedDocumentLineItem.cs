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
        decimal total)
    {
        Id = id;
        ItemName = itemName;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountPercent = discountPercent;
        DiscountAmount = discountAmount;
        Total = total;
    }

    private ReviewedDocumentLineItem() { }

    public Guid Id { get; private set; }
    public string ItemName { get; private set; } = null!;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal? DiscountPercent { get; private set; }
    public decimal DiscountAmount { get; private set; }
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
        decimal total) =>
        new(Guid.NewGuid(), itemName.Trim(), quantity, unitPrice, discountPercent, discountAmount, total);
}
