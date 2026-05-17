using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Expenses;

public static class PaymentErrors
{
    public static readonly Error NotFound = new("Payment.NotFound", "Payment not found");
    public static readonly Error DocumentAlreadyHasPayment = new("Payment.DocumentHasPayment", "Document already has a payment recorded");
    public static readonly Error AlreadyProcessed = new("Payment.AlreadyProcessed", "Payment has already been processed");
    public static readonly Error TenantRequired = new("Payment.TenantRequired", "Tenant ID is required");
    public static readonly Error DocumentIdRequired = new("Payment.DocumentIdRequired", "Document ID is required");
    public static readonly Error DepartmentRequired = new("Payment.DepartmentRequired", "Department ID is required");
    public static readonly Error InvalidAmount = new("Payment.InvalidAmount", "Amount must be greater than zero");
    public static readonly Error RecordedByRequired = new("Payment.RecordedByRequired", "Recorded by membership ID is required");
    public static readonly Error ConfirmedByRequired = new("Payment.ConfirmedByRequired", "Confirmed by membership ID is required");
    public static readonly Error RejectedByRequired = new("Payment.RejectedByRequired", "Rejected by membership ID is required");
    public static readonly Error RejectionReasonRequired = new("Payment.RejectionReasonRequired", "Rejection reason is required");
    public static readonly Error DocumentNotApproved = new("Payment.DocumentNotApproved", "Document must be approved before recording payment");
    public static readonly Error RejectionTypeRequired = new("Payment.RejectionTypeRequired", "Rejection type is required");
    public static readonly Error InvalidExchangeRate = new("Payment.InvalidExchangeRate", "Exchange rate must be greater than zero");
    public static readonly Error SameCurrencyRequiresUnitRate = new("Payment.SameCurrencyRequiresUnitRate", "When payment currency matches base currency, exchange rate must be 1.0");
    public static readonly Error UpdateRequiresPending = new("Payment.UpdateRequiresPending", "Only pending payments can be updated");
    public static readonly Error CancelRequiresPending = new("Payment.CancelRequiresPending", "Only pending payments can be cancelled");
    public static readonly Error CancellationReasonRequired = new("Payment.CancellationReasonRequired", "Cancellation reason is required");
    public static readonly Error CancellationReasonTooLong = new("Payment.CancellationReasonTooLong", "Cancellation reason must be 500 characters or less");
    public static readonly Error UpdatedByRequired = new("Payment.UpdatedByRequired", "Updated by membership ID is required");
    public static readonly Error CancelledByRequired = new("Payment.CancelledByRequired", "Cancelled by membership ID is required");
    public static readonly Error InvalidPaymentMethod = new("Payment.InvalidMethod", "Payment method is not supported");
    public static readonly Error RefundRequiresConfirmed = new("Payment.RefundRequiresConfirmed", "Only confirmed payments can be refunded");
    public static readonly Error RefundAmountInvalid = new("Payment.RefundAmountInvalid", "Refund amount must be greater than zero and not exceed the payment amount");
    public static readonly Error RefundReasonRequired = new("Payment.RefundReasonRequired", "Refund reason is required");
    public static readonly Error RefundReasonTooLong = new("Payment.RefundReasonTooLong", "Refund reason must be 500 characters or less");
    public static readonly Error RefundPaymentIdRequired = new("Payment.RefundPaymentIdRequired", "Refund payment ID is required");
    public static readonly Error RefundInitiatedByRequired = new("Payment.RefundInitiatedByRequired", "Refund initiated-by membership ID is required");
    public static readonly Error RefundNotPending = new("Payment.RefundNotPending", "Refund is not pending");
    public static readonly Error RefundFailureReasonRequired = new("Payment.RefundFailureReasonRequired", "Refund failure reason is required");
    public static readonly Error AlreadyRefunded = new("Payment.AlreadyRefunded", "Payment has already been refunded");
}
