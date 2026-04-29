using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Documents.Commands.SaveManualDraft;

public sealed record SaveManualDraftLineItem(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total
);

public sealed record SaveManualDraftCommand(
    Guid TenantId,
    Guid MembershipId,
    string OriginalFileName,
    string VendorName,
    string Reference,
    DateOnly DocumentDate,
    DateOnly DueDate,
    string Category,
    string? VendorTaxId,
    decimal Subtotal,
    decimal Vat,
    decimal TotalAmount,
    string ReviewedByStaff,
    IReadOnlyList<SaveManualDraftLineItem> LineItems
) : ICommand<Result<Guid>>;