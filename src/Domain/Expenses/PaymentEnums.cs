namespace FinFlow.Domain.Expenses;

public enum PaymentMethod
{
    Cash,
    BankTransfer,
    Check,
    CreditCard,
    Payroll,
    Other
}

public enum PaymentStatus
{
    Pending,
    Confirmed,
    Rejected,
    Cancelled
}

public enum PaymentRejectType
{
    InsufficientDocumentation,
    DuplicateClaim,
    PolicyViolation,
    InvalidAmount,
    NotReimbursable,
    Other
}
