using FinFlow.Domain.Expenses;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class PaymentRefundRepository : IPaymentRefundRepository
{
    private readonly ApplicationDbContext _dbContext;

    public PaymentRefundRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(PaymentRefund refund) => _dbContext.PaymentRefunds.Add(refund);

    public Task<PaymentRefund?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.PaymentRefunds.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<bool> ExistsByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default) =>
        _dbContext.PaymentRefunds.AnyAsync(r => r.PaymentId == paymentId, cancellationToken);
}
