namespace FinFlow.Domain.Expenses;

public interface IPaymentRefundRepository
{
    void Add(PaymentRefund refund);
    Task<PaymentRefund?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default);
}
