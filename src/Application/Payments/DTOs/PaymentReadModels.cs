namespace FinFlow.Application.Payments.DTOs;

public sealed record PaymentQueueItemResponse(
    Guid? PaymentId,
    Guid DocumentId,
    string Reference,
    string DocumentFileName,
    string EmployeeName,
    string EmployeeMembershipId,
    string? EmployeeCode,
    string? MerchantName,
    string Department,
    decimal Amount,
    string CurrencyCode,
    decimal AmountInBaseCurrency,
    DateOnly ExpenseDate,
    DateTime SubmittedAt,
    string QueueStatus,
    string? PaymentMethod,
    DateTime? RecordedAt,
    DateTime? ConfirmedAt,
    string? RejectionReason,
    string? Notes);

public sealed record PaymentAuditTrailItemResponse(
    string Type,
    string Title,
    string Actor,
    DateTime Timestamp,
    string? Note);

public sealed record PaymentDocumentLineItemResponse(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal? DiscountPercent = null,
    decimal DiscountAmount = 0m,
    decimal? TaxRate = null,
    decimal TaxableAmount = 0m,
    decimal TaxAmount = 0m,
    decimal Total = 0m);

public sealed record PaymentDocumentTaxLineResponse(
    string TaxType,
    decimal? Rate,
    decimal TaxableAmount,
    decimal TaxAmount);

public sealed record PaymentDetailResponse(
    Guid? PaymentId,
    Guid DocumentId,
    string Reference,
    string? SettlementRef,
    string ApprovalRecordId,
    string EmployeeName,
    string EmployeeMembershipId,
    string? EmployeeCode,
    string? MerchantName,
    string Department,
    string? CostCenter,
    decimal Amount,
    decimal Subtotal,
    decimal? DocumentDiscountPercent,
    decimal DocumentDiscountAmount,
    decimal Vat,
    decimal TotalAmount,
    string CurrencyCode,
    decimal AmountInBaseCurrency,
    DateOnly ExpenseDate,
    string? PaymentMethod,
    string QueueStatus,
    string DocumentFileName,
    string? DocumentDownloadUrl,
    string? DocumentViewUrl,
    IReadOnlyList<PaymentDocumentLineItemResponse> LineItems,
    IReadOnlyList<PaymentDocumentTaxLineResponse> TaxLines,
    IReadOnlyList<PaymentAuditTrailItemResponse> AuditTrail,
    string? MethodSource,
    bool MethodEditable);
