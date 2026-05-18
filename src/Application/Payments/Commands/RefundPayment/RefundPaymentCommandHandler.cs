using FinFlow.Application.Budgets.Services;
using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using MediatR;

namespace FinFlow.Application.Payments.Commands.RefundPayment;

internal sealed class RefundPaymentCommandHandler : IRequestHandler<RefundPaymentCommand, Result<PaymentRefundResponse>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentRefundRepository _paymentRefundRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly IBudgetReservationService _budgetReservation;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;

    public RefundPaymentCommandHandler(
        IPaymentRepository paymentRepository,
        IPaymentRefundRepository paymentRefundRepository,
        IExpenseRepository expenseRepository,
        IBudgetReservationService budgetReservation,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _paymentRefundRepository = paymentRefundRepository;
        _expenseRepository = expenseRepository;
        _budgetReservation = budgetReservation;
        _currentTenant = currentTenant;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PaymentRefundResponse>> Handle(RefundPaymentCommand request, CancellationToken cancellationToken)
    {
        if (!_currentTenant.MembershipId.HasValue)
            return Result.Failure<PaymentRefundResponse>(new Error("Payment.MembershipContext", "Membership context is not available."));

        var payment = await _paymentRepository.GetEntityByIdAsync(request.PaymentId, cancellationToken);
        if (payment is null)
            return Result.Failure<PaymentRefundResponse>(PaymentErrors.NotFound);

        var alreadyRefunded = await _paymentRefundRepository.ExistsByPaymentIdAsync(payment.Id, cancellationToken);
        if (alreadyRefunded)
            return Result.Failure<PaymentRefundResponse>(PaymentErrors.AlreadyRefunded);

        var refundResult = payment.InitiateRefund(request.Amount, request.Reason, _currentTenant.MembershipId.Value);
        if (refundResult.IsFailure)
            return Result.Failure<PaymentRefundResponse>(refundResult.Error);

        var refund = refundResult.Value;
        _paymentRefundRepository.Add(refund);
        _paymentRepository.Update(payment);

        // Reject the linked expense (if any) so reporting reflects the rollback.
        var matchingExpense = await _expenseRepository.GetByPaymentIdAsync(payment.Id, cancellationToken);
        if (matchingExpense is not null)
        {
            var expenseEntity = await _expenseRepository.GetEntityByIdAsync(matchingExpense.Id, cancellationToken);
            if (expenseEntity is not null && expenseEntity.Status == ExpenseStatus.Confirmed)
            {
                expenseEntity.Reject($"Refunded: {refund.Reason}", _currentTenant.MembershipId.Value);
                _expenseRepository.Update(expenseEntity);

                // Reduce the spent pool by exactly the refund amount.
                // Decimal-precise — no SQL SUM, no stale-by-one bug.
                var releaseResult = await _budgetReservation.ReverseSpentAsync(
                    new BudgetMovement(
                        TenantId: expenseEntity.IdTenant,
                        DepartmentId: expenseEntity.IdDepartment,
                        Month: expenseEntity.Month,
                        Year: expenseEntity.Year,
                        AmountInBaseCurrency: refund.Amount * payment.ExchangeRate,
                        SourceEntityId: refund.Id,
                        SourceEntityType: "PaymentRefund",
                        Reason: $"Refunded: {refund.Reason}"),
                    cancellationToken);
                if (releaseResult.IsFailure)
                    return Result.Failure<PaymentRefundResponse>(releaseResult.Error);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new PaymentRefundResponse(
            refund.Id,
            refund.PaymentId,
            refund.Amount,
            refund.Reason,
            refund.Status.ToString(),
            refund.InitiatedByMembershipId,
            refund.InitiatedAt));
    }
}
