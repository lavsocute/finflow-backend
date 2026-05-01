namespace FinFlow.Api.GraphQL.Payments;

public sealed class PaymentPayload
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = null!;
    public decimal AmountInVnd { get; set; }
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
        AmountInVnd = response.AmountInVnd,
        PaymentMethod = response.PaymentMethod,
        Status = response.Status,
        RecordedAt = response.RecordedAt,
        RecordedByMembershipId = response.RecordedByMembershipId,
        Notes = response.Notes
    };
}