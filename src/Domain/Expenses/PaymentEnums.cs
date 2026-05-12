namespace FinFlow.Domain.Expenses;

public enum CurrencyCode
{
    VND,
    USD,
    EUR,
    GBP,
    JPY,
    SGD
}

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
    Rejected
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
