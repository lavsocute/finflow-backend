using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public sealed class UploadedDocumentDraftLineItem
{
    private UploadedDocumentDraftLineItem(Guid id, string itemName, decimal quantity, decimal unitPrice, decimal total)
    {
        Id = id;
        ItemName = itemName;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Total = total;
    }

    private UploadedDocumentDraftLineItem() { }

    public Guid Id { get; private set; }
    public string ItemName { get; private set; } = null!;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Total { get; private set; }

    public static Result<UploadedDocumentDraftLineItem> Create(string itemName, decimal quantity, decimal unitPrice, decimal total)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineItemNameRequired);

        if (quantity <= 0)
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineItemQuantityInvalid);

        if (unitPrice <= 0)
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineItemUnitPriceInvalid);

        if (total <= 0)
            return Result.Failure<UploadedDocumentDraftLineItem>(UploadedDocumentDraftErrors.LineItemTotalInvalid);

        return Result.Success(new UploadedDocumentDraftLineItem(Guid.NewGuid(), itemName.Trim(), quantity, unitPrice, total));
    }
}
