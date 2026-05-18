namespace FinFlow.Domain.Expenses;

public record PaymentSummary(
    Guid Id,
    Guid IdTenant,
    Guid DocumentId,
    Guid IdDepartment,
    decimal Amount,
    string CurrencyCode,
    decimal ExchangeRate,
    decimal AmountInBaseCurrency,
    string BaseCurrencyCode,
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

    /// <summary>
    /// Bulk-load tracked payment entities by ID list, scoped to the given tenant.
    /// Used by batch operations (e.g. CSV export) that need to validate/update many at once.
    /// </summary>
    Task<IReadOnlyList<Payment>> GetByIdsAsync(IReadOnlyList<Guid> ids, Guid tenantId, CancellationToken cancellationToken = default);

    void Add(Payment payment);
    void Update(Payment payment);
}