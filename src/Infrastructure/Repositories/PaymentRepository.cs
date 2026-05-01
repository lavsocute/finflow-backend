using FinFlow.Domain.Expenses;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class PaymentRepository : IPaymentRepository
{
    private readonly ApplicationDbContext _dbContext;

    public PaymentRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<PaymentSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Payment>()
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PaymentSummary(
                p.Id,
                p.IdTenant,
                p.DocumentId,
                p.IdDepartment,
                p.Amount,
                p.CurrencyCode,
                p.ExchangeRate,
                p.AmountInVnd,
                p.RecordedByMembershipId,
                p.RecordedAt,
                p.Method,
                p.Status,
                p.ConfirmedByMembershipId,
                p.ConfirmedAt,
                p.RejectionReason,
                p.Notes,
                p.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PaymentSummary?> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Payment>()
            .AsNoTracking()
            .Where(p => p.DocumentId == documentId)
            .Select(p => new PaymentSummary(
                p.Id,
                p.IdTenant,
                p.DocumentId,
                p.IdDepartment,
                p.Amount,
                p.CurrencyCode,
                p.ExchangeRate,
                p.AmountInVnd,
                p.RecordedByMembershipId,
                p.RecordedAt,
                p.Method,
                p.Status,
                p.ConfirmedByMembershipId,
                p.ConfirmedAt,
                p.RejectionReason,
                p.Notes,
                p.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PaymentSummary>> GetByTenantIdAsync(Guid idTenant, PaymentStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Payment>()
            .AsNoTracking()
            .Where(p => p.IdTenant == idTenant);

        if (status.HasValue)
            query = query.Where(p => p.Status == status.Value);

        return await query
            .OrderByDescending(p => p.RecordedAt)
            .Select(p => new PaymentSummary(
                p.Id,
                p.IdTenant,
                p.DocumentId,
                p.IdDepartment,
                p.Amount,
                p.CurrencyCode,
                p.ExchangeRate,
                p.AmountInVnd,
                p.RecordedByMembershipId,
                p.RecordedAt,
                p.Method,
                p.Status,
                p.ConfirmedByMembershipId,
                p.ConfirmedAt,
                p.RejectionReason,
                p.Notes,
                p.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentSummary>> GetPendingByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default) =>
        await GetByTenantIdAsync(idTenant, PaymentStatus.Pending, cancellationToken);

    public async Task<bool> ExistsByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Payment>()
            .AnyAsync(p => p.DocumentId == documentId, cancellationToken);

    public async Task<Payment?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Payment>()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public void Add(Payment payment) => _dbContext.Set<Payment>().Add(payment);
    public void Update(Payment payment) => _dbContext.Set<Payment>().Update(payment);
}