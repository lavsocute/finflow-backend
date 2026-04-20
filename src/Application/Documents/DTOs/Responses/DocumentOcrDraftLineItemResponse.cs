namespace FinFlow.Application.Documents.DTOs.Responses;

public sealed record DocumentOcrDraftLineItemResponse(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total
);
