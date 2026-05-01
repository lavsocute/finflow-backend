namespace FinFlow.Domain.Expenses;

public record PaymentSummary(
    Guid Id,
    Guid IdTenant,
    Guid DocumentId,
    Guid IdDepartment,
    decimal Amount,
    CurrencyCode CurrencyCode,
    decimal ExchangeRate,
    decimal AmountInVnd,
    Guid RecordedByMembershipId,
    DateTime RecordedAt,
    PaymentMethod Method,
    PaymentStatus Status,
    Guid? ConfirmedByMembershipId,
    DateTime? ConfirmedAt,
    string? RejectionReason,
    string? Notes,
    DateTime CreatedAt);

public interface IPaymentRepository
{
    Task<PaymentSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PaymentSummary?> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentSummary>> GetByTenantIdAsync(Guid idTenant, PaymentStatus? status = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentSummary>> GetPendingByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default);
    Task<bool> ExistsByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<Payment?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(Payment payment);
    void Update(Payment payment);
}