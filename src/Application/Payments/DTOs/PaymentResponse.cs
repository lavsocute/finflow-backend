namespace FinFlow.Application.Payments.DTOs;

public sealed record PaymentResponse(
    Guid Id,
    Guid DocumentId,
    decimal Amount,
    string CurrencyCode,
    decimal AmountInBaseCurrency,
    string BaseCurrencyCode,
    decimal ExchangeRate,
    string PaymentMethod,
    string Status,
    DateTime RecordedAt,
    Guid RecordedByMembershipId,
    string? Notes);