namespace FinFlow.Api.GraphQL.Payments;

public sealed class PaymentPayload
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = null!;
    public decimal AmountInBaseCurrency { get; set; }
    public string BaseCurrencyCode { get; set; } = null!;
    public decimal ExchangeRate { get; set; }
    public string PaymentMethod { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime RecordedAt { get; set; }
    public Guid RecordedByMembershipId { get; set; }
    public string? Notes { get; set; }

    public static PaymentPayload FromResponse(Application.Payments.DTOs.PaymentResponse response) => new()
    {
        Id = response.Id,
        DocumentId = response.DocumentId,
        Amount = response.Amount,
        CurrencyCode = response.CurrencyCode,
        AmountInBaseCurrency = response.AmountInBaseCurrency,
        BaseCurrencyCode = response.BaseCurrencyCode,
        ExchangeRate = response.ExchangeRate,
        PaymentMethod = response.PaymentMethod,
        Status = response.Status,
        RecordedAt = response.RecordedAt,
        RecordedByMembershipId = response.RecordedByMembershipId,
        Notes = response.Notes
    };
}


public sealed class PaymentRefundPayload
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = null!;
    public string Status { get; set; } = null!;
    public Guid InitiatedByMembershipId { get; set; }
    public DateTime InitiatedAt { get; set; }

    public static PaymentRefundPayload FromResponse(Application.Payments.DTOs.PaymentRefundResponse response) => new()
    {
        Id = response.Id,
        PaymentId = response.PaymentId,
        Amount = response.Amount,
        Reason = response.Reason,
        Status = response.Status,
        InitiatedByMembershipId = response.InitiatedByMembershipId,
        InitiatedAt = response.InitiatedAt
    };
}
