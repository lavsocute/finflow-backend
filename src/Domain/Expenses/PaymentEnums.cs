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
    Other
}

public enum PaymentStatus
{
    Pending,
    Confirmed,
    Rejected
}