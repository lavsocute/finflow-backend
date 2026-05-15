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
}
