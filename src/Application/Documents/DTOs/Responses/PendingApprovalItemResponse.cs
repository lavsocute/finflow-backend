namespace FinFlow.Application.Documents.DTOs.Responses;

public sealed record PendingApprovalItemResponse(
    Guid DocumentId,
    string Title,
    string VendorName,
    string Requester,
    string RequesterEmail,
    string Department,
    decimal Amount,
    string Currency,
    DateOnly ExpenseDate,
    DateTime SubmittedAt,
    string Priority,
    string Status,
    string? PolicySummary);

public sealed record ApprovalDetailResponse(
    Guid DocumentId,
    string RequestCode,
    string Title,
    string VendorName,
    string RequesterName,
    string RequesterEmail,
    string Department,
    decimal Amount,
    string Currency,
    decimal Subtotal,
    decimal? DocumentDiscountPercent,
    decimal DocumentDiscountAmount,
    decimal Vat,
    decimal TotalAmount,
    DateOnly ExpenseDate,
    DateTime SubmittedAt,
    string Priority,
    string Status,
    string? PolicySummary,
    IReadOnlyList<ApprovalLineItemResponse> LineItems,
    IReadOnlyList<DocumentTaxLineResponse> TaxLines);

public sealed record ApprovalLineItemResponse(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal? DiscountPercent = null,
    decimal DiscountAmount = 0m,
    decimal? TaxRate = null,
    decimal TaxableAmount = 0m,
    decimal TaxAmount = 0m,
    decimal Total = 0m);

public sealed record ApprovalQueueItemResponse(
    Guid DocumentId,
    string Title,
    string VendorName,
    string Requester,
    string RequesterEmail,
    string Department,
    decimal Amount,
    string Currency,
    DateOnly ExpenseDate,
    DateTime SubmittedAt,
    string Priority,
    string Status,
    string? PolicySummary);

public sealed record ApprovalQueueResponse(
    IReadOnlyList<ApprovalQueueItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record ExportApprovalQueueResponse(
    string FileName,
    string DownloadUrl);
